# Editable stacks + free-orientation slots

Two product changes on top of the current WPF app:

1. **Edit any generated collage** — modes still generate stacks/decks; Manual tools apply to the focused item without leaving that mode. Run asks **export current** vs **export everything** (selection rules below).
2. **Free-orientation slot fill** — when placing or swapping photos into slots by hand, landscape may go in portrait cells and vice versa; cover-crop fills the cell (existing pan/flip/gray still work). Auto job generation keeps today’s orientation filters so random/batch/combo pools stay layout-correct.

## Goals (acceptance)

- Single / batch / random / combo still build candidates the same way.
- Focusing a deck card (or the single preview) lets you edit slots with today’s Manual gestures: assign from library, clear, Ctrl+swap, pan, flip, grayscale, undo/redo.
- Edits persist on that card while you browse other cards and come back.
- Changing mode / layout / folder / count / borderless / combo that **rebuilds** candidate identity clears per-card edits (same hard-rebuild rule as today’s deck). Soft refreshes (color, bleed) keep edits.
- Run presents a clear choice: **this collage only** or **all exportable collages** (see Export policy).
- Manual slot assign no longer rejects wrong orientation; preview and export cover-crop to the cell.
- CLI generation behavior unchanged unless noted (orientation pool filter stays for auto jobs).

## Non-goals

- Redesigning layout geometry or Instagram canvas size.
- Letting auto batch/random/combo *initially* pick wrong-orientation photos into jobs.
- Per-slot orientation rules that differ by cell inside one layout (all cells of a layout stay the same aspect; only the *source photo* orientation is free when editing).
- Multi-window or destructive “fork collage” UI beyond deck cards.

---

## Current baseline (what we change)

| Area | Today |
|------|--------|
| Modes | `SelectedMode`: single / batch / random / combo / **manual** (island) |
| Deck | Shown for combo/batch/random when jobCount > 1; cards hold paths + layout + borderless, **no slots** |
| Stage | Non-manual: `RenderPreview` (paths, default pans). Manual: `RenderManual` + `_manualSlots` |
| Run | Manual → one JPEG with slots. Else → ticked deck jobs (or all jobs if no deck), **`slots: null`** |
| Orientation | Enforced in scanner, assign, validate, `ProcessImageForCell` / preview |

Key files: `MainWindow.xaml(.cs)`, `DeckService` / `DeckCardItem`, `PreviewStageService`, `ExportService`, `SlotAssignment`, `ImagePipeline`, `OrientationHelper`, `CollageExporter`, `CollagePreviewRenderer`.

---

## Product model

### Editable collage state

Every on-stage collage (deck card or single-job preview) owns:

- Identity: layout, borderless, ordered paths (as today)
- **Optional** `SlotAssignment?[]` — same length as slot count  
  - `null` entry = empty (only when user cleared a slot while editing; generated cards start **filled**)  
  - On first open of a generated card, materialize assignments from job paths (pan/flip/gray defaults; `grid-1x2-v` keeps −1 / +1 pans)

Deck card stores this state. Single-job / no-deck mode stores one parallel “focused collage” object with the same shape.

### Mode “Manual”

Prefer **fold Manual into the same editor** rather than keeping a forever-separate mode:

- **Blank start:** keep a way to open an empty N-slot collage for the selected layout (today’s Manual). Implementation options (pick one in Phase 1 and stick to it):  
  - **A (recommended):** keep a Manual radio that creates one empty editable collage (no deck), same stage gestures as editing a generated card.  
  - **B:** drop the radio; add “New blank collage” that switches to single + empty slots.
- “Use in manual” becomes **“Edit as blank / copy to editable collage”** or simply focuses the card and enables the same tools (rename UI copy).

Either way: one gesture/tool stack, one preview path (`RenderManual` / slots-aware export), not two products.

### Export policy

On Run, after validation, show a choice (MessageBox / small dialog):

| Choice | Behavior |
|--------|----------|
| **Export current** | Write only the focused collage (deck focus, or the only collage in single/manual). Use that collage’s slot assignments. |
| **Export all** | Deck visible: export **all ticked** cards (today’s tick model). If none ticked, offer to select all or cancel (do not silently no-op). No deck (single / one job / blank Manual): same as current — one file. |

Filename: keep layout-index naming for generated jobs; blank Manual may keep `manual_…` or use layout prefix — decide in Phase 3 and document in README.

Progress / cancel / busy gating stay as today.

---

## Phase 1 — Shared editable collage + stage

Introduce a small App (or Core) model, e.g. `EditableCollage` / extend `DeckCardItem`:

- `Layout`, `Borderless`, `Paths`, `Slots` (`List<SlotAssignment?>` or materialized non-null for filled jobs)
- Factory: `FromJob(ExportJob)` → filled slots from paths + layout defaults
- Factory: `Blank(layout, borderless)` → empty slots

Wire stage so **any mode** uses the slots-aware preview path when the focused collage has slot state (always, once materialized):

- `PreviewStageService`: stop forking “manual vs candidates” for editing; focused collage drives `RenderManual`-style preview (live pan supported).
- Prev/Next and deck focus switch which `EditableCollage` is bound; preserve each card’s slots in memory.
- Hard rebuild of deck: drop slot edits with card identity (same triggers as today). Soft refresh: keep slots; recolor/bleed only.

Manual gestures currently gated on `SelectedMode == "manual"` move to **“stage has an editable focus”** (always true when a collage is shown).

Done when: generate combo/batch/random/single → focus a card → pan/flip/swap → switch to another card → return → edits still there; blank Manual (or blank collage) still works.

Verify: App smoke on stack-3 batch (≥2 cards) and combo; undo stack scoped per collage or clear on focus change (pick one; prefer **per-collage undo**).

---

## Phase 2 — Free orientation on manual slot fill

**Keep** orientation filtering for:

- `ImageScanner.GetValidPaths` / combination generators / CLI job pools  
- Auto preview of *untouched* generated jobs may still assume pool photos match (they will)

**Remove** orientation rejection for:

- `AssignToSlot` / drag-assign in the App  
- `ExportService.ValidateRun` slot orientation checks  
- `ImagePipeline.ProcessImageForCell` and `ProcessImageForPreviewCell` when processing a **slot-driven** cell (or always: cover already fits any aspect — safest is **drop the MatchesOrientation gate inside ProcessImage\*** and rely on scanner only for auto pools)

Manual preview already shows a gray cell on process null — after the change, mismatched photos must render and export via cover-crop.

Update status copy / Shortcuts if they mention orientation requirements for Manual.

Done when: on stack-3 (H), assign a portrait into a slot → preview fills the strip; export JPEG looks correct; auto batch still only *builds* from landscapes.

Verify: unit test ProcessImage(portrait path, Horizontal layout cell size) returns non-null exact cell size; App assign smoke; existing combination tests still pass (pool filter unchanged).

---

## Phase 3 — Run: current vs all

- Replace silent “ticked only / or all jobs” with an explicit dialog when there is more than one possible export target **or** always show a two-button dialog when a deck is visible.
- **Export current:** one job, focused collage slots.
- **Export all:** ticked cards each with their own slots; pass `slots` into `CollageExporter` / `ExportJobRunner` (today passes null).
- Single collage: dialog optional (or only “Export”); still one file.
- Validation: current = that collage complete (all slots non-null, files exist). All = each ticked collage complete; list failures.

Done when: edit two combo cards differently → Export all writes both with distinct crops; Export current writes only the focused one.

Verify: App combo with two edited cards; compare outputs; CLI untouched.

---

## Phase 4 — UI copy, settings, tests, docs

- Mode row / status strings: Manual is “blank collage” or remains a mode per Phase 1 choice; remove “island” wording (“switch to Manual to edit”).
- Shortcuts window: document edit-any-stack + Ctrl+swap + export choice + free orientation on place.
- README: short “Editing” section.
- Tests: slot persistence across soft deck refresh; export runner with non-null slots; orientation-free process path; regression that scanner still filters H/V for combinations.
- Settings: no new keys required unless persisting edits across app restart (out of scope unless trivial).

Done when: `dotnet test` green; README + Shortcuts match behavior.

---

## Suggested implementation order

1. Phase 2 can ship slightly early (orientation) if Phase 1 model is not ready — but assign UX still lives on Manual-only until Phase 1 lands. Prefer **Phase 1 → 2 → 3 → 4** so free-orientation is tested on the unified editor.
2. Keep Core export API additive: `ExportCollage(..., slots)`; runner accepts optional per-job slots.
3. Do not weaken CLI orientation pools in the same PR as App editing unless a follow-up explicitly asks for it.

## Estimated duration

- Phase 1: 8–12 agent-hours (model + stage/deck wiring + undo scoping)
- Phase 2: 2–4 agent-hours
- Phase 3: 4–6 agent-hours
- Phase 4: 3–5 agent-hours

## Risks

- **Memory:** many combo cards × full slot clones — keep path strings + pans only (already small).
- **Preview cost:** every focus uses slots renderer — reuse existing throttle/cancel from manual pan work.
- **Tick + edit confusion:** Export all = ticked only must stay obvious in the dialog subtitle (“N ticked”).
- **Hard rebuild surprise:** document that changing layout/mode wipes per-card edits (same as regenerating the deck today).
