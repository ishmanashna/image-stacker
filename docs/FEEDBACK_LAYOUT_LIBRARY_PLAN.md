# Feedback: layouts, library, bleed, effects

One-sentence outcome: option text is white, layout cards tell in/out/grid, the deck stops remixing, photos can be picked (nested folders, color vs mono), Grid 2×2 H and Stack 1 export the right shape, bleed softens seams, and Noise/Orton show on the stage.

## Scope

In: every item from the playtest list except a larger folder-picker window (see waiver).

Out: changing the combo pack menu; canvas sizes other than 3840×4800 and 4800×3840; persisting include/exclude or effects across restarts; native camera-pixel Stack 1 exports.

Waiver: `OpenFolderDialog` has no size. Leave the system picker. Do not add Win32 folder-dialog hacks.

## Decisions (locked)

- **White text, not “labels”.** The words beside radios and checkboxes (`Content`), the Mode/Count/Color text in that pane, combo items, and GroupBox headers in Mode & style / Run use `Foreground="#FFFFFF"`. Same for deck tick checkboxes if they are still dark. Do not rely on system theme.
- **Stack 1 “original H or V”** means the **output orientation** matches the photo (portrait canvas 3840×4800 or landscape 4800×3840), cover-filled. Not the camera’s pixel size. JPEG still ≤8 MB.
- **Job identity** is layout name + ordered photo paths + job index. Color, bleed, borderless, noise, and Orton are attributes on the collage. Toggling them must not call `BuildJobs` / reshuffle. `EditableCollage.MatchesJob` must not treat borderless as identity.
- **Deck list lives in memory for the session.** `BuildJobs` runs when folder, mode, layout, count, or include-set change (and on first show). Focus / prev / next / color / bleed / effects only render the existing `DeckCardItem`.
- **Catalog canvas:** `LayoutDefinition` gets `bool LandscapeCanvas` (default false). `grid-2x2-h` is true. `LayoutGeometry` gets `CanvasWidth` / `CanvasHeight` (4800×3840 vs 3840×4800). `Compute(layout, borderless, bleed, paths?: IReadOnlyList<string>)` — all layouts except `stack-1` ignore paths for canvas; `stack-1` uses `paths[0]` after EXIF (taller → portrait, wider → landscape, square → portrait).
- **AxB on cards:** `grid-*` parse N×M from the id (`grid-2x4` → `2×4`). Stacks use catalog `Rows×Cols` (`stack-3` → `3×1`). `stack-1` → `1×1`. Input: H / V / any from `LayoutOrientation`. Output: H if canvas width > height else V.
- **Delete `grid-1x3-h`.** Map settings `layout` `grid-1x3-h` → `grid-1x3-v` in `AppSettingsStore.Load`. Do not change `ComboJobSpecs.All`.
- **Include:** checkbox on each thumb, default on. Dim when off. Auto jobs and “click thumb to fill next empty slot” skip excluded paths. Drag onto a slot may still place that file. Include map keyed by full path; files past the thumb cap stay included.
- **Color vs mono:** four buttons under Photos — Color on, Color off, Mono on, Mono off. Mono if `max(R,G,B) - min(R,G,B) < 28` on thumb (or 64px shrink in tests). Faded B&W counts; a weak tint is color.
- **Bleed:** keep geometry grow. Change only `ComposeCanvas`: alpha ramp along overlap, width 16 px at full canvas, scaled in preview. Slot order unchanged. Borderless ⇒ no bleed.
- **Noise / Orton:** bools on `EditableCollage`, off by default. Apply **once after** `ComposeCanvas` in export and both preview renderers. Fixed look (grain mix ~0.12; Orton blur ~3% of min canvas side, screen ~50%). Sidebar toggles. Undo snapshots flags with slots. Toggles must not `BuildJobs`.
- **Encoding:** `<?xml version="1.0" encoding="utf-8"?>` on App XAML. Non-ASCII punctuation as XML numeric entities in XAML and `\uXXXX` in C# UI strings. ASCII fallbacks allowed (`...`, `x`, `<` `>`).
- **Preview group header** is `Preview` (drop `4:5`).
- **Bright colors** in `UiConstants.ColorPresets` and `ColorParser` as needed: red, lime (`#00FF00` as lime), yellow, orange, magenta; keep white, black, beige, ivory, gray, light gray, dark gray, wheat, tan, navy, maroon.

## Phase 1 — White text and un-garbled UI

Touch: `App.xaml`, `MainWindow.xaml`, `ShortcutsWindow.xaml.cs`, any MainWindow C# strings for nav/deck/run (`…`, `—`, `◀`, `▶`, `×`).

Styles: RadioButton, CheckBox, ComboBox, ComboBoxItem, GroupBox.Header, TextBlock in the right column — `Foreground="#FFFFFF"`. Replace fancy glyphs with entities/`\u`. Header Photos (no “scroll”). Color presets as locked. Leave `PickFolder` as-is.

Done when: option **text** is white; Browse/Shortcuts/arrows/× look like those words/symbols, not mojibake; Photos header clean; dropdown has bright colors.

How to verify: build App, read Mode & style and Browse. No full tests.

## Phase 2 — Remove Row 1×3 H

Touch: `LayoutConfig.cs`, `UiConstants.cs`, `AppSettingsStore.cs`, `Program.cs` help, `CoreTests` 1×3 facts, CLI layout list.

Delete `grid-1x3-h` from catalog and UI order. Load-map stale settings. Replace tests that required both ids. Do not edit `ComboJobSpecs`.

Done when: grep has no catalog/UI/CLI id except a test that it is **absent**; combo still four specs.

How to verify: targeted catalog/combo tests only.

## Phase 3 — Layout cards in / out / AxB

Touch: `LayoutDefinition` (`LandscapeCanvas`), `UiConstants.LayoutCardText` or a small helper that builds the three lines from catalog, `BuildLayoutCards`.

Card text: name, then `in H|V|any`, `out H|V`, `AxB` per locked rules. `grid-2x2-h` already `LandscapeCanvas = true` here so **out H** is honest before export uses it.

Done when: layout grid shows those three facts; Grid 2×4 reads `2×4` not `4×2`.

How to verify: build App, look at cards. One unit test for AxB helper if you add a helper.

## Phase 4 — Deck click does not rebuild jobs

Touch: `MainWindow.xaml.cs` (`SchedulePreviewRefresh`, `OnDeckFocusIndexChanged`, `RebuildDeck`), `DeckService.cs`.

Split identity rebuild from stage render. While the deck is visible, focus / selection / prev-next update `FocusIndex` and render `GetFocusedCollage()` only. Do not call `ExportService.BuildJobs` on that path. `HardRebuild` sets `FocusIndex = 0` only when the new list is empty or shorter than the old index; otherwise keep index.

Done when: combo deck, click card 5, stage is that card; back to 1 then 5, same photos.

How to verify: App combo. Do not run full tests.

## Phase 5 — Color, bleed, borderless do not reshuffle

Touch: `EditableCollage.MatchesJob`, `DeckService.RefreshOrRebuild` / `SoftRefresh`, `SchedulePreviewRefresh` for option/color handlers.

Identity ignores borderless. Soft path updates color/bleed/borderless on existing cards and refreshes bitmaps. `BuildJobs` still not on those handlers.

Done when: combo deck, change color or bleed, same cards in the same order; borderless updates look, not the photo set; slot edits survive.

How to verify: App combo. Optional: `MatchesJob` unit-style assert in App is not required if Core job compare lives in DeckService tests — skip if WPF-only; then App smoke is enough.

## Phase 6 — Recursive scan

Touch: `ImageScanner.cs`, `ThumbnailLoader.cs`, tests.

`EnumerateFiles(..., SearchOption.AllDirectories)`. Same extensions, orientation filter, ordinal sort. Nested jpeg appears in scan and thumbs.

Done when: test folder with `sub/a.jpg` is returned; thumbs load it.

How to verify: one scanner test; no full suite.

## Phase 7 — Candidates from a path list

Touch: `LayoutContracts.cs`, `CombinationGenerator` (only if needed), `ExportService.cs`, CLI still scans then passes full list.

Add `ListLayoutCandidatesFromPaths` / combo-from-paths. Folder overloads become scan + that. App `BuildJobs` takes included paths (all included until Phase 8). CLI: scan recursive, pass all.

Done when: App and CLI still generate the same jobs as before this phase for an unfiltered folder; Core can generate from a subset list in a test.

How to verify: existing combination tests + one subset-list test.

## Phase 8 — Photo include / exclude

Touch: `ThumbnailItem`, thumb template, `MainWindow` assign/fill-next, `ExportService.BuildJobs` callers, include dictionary by path.

Checkbox on each thumb, default on. Dim excluded. Filter auto pools. Next-empty-slot skip excluded. Drag-assign allowed. Rebuild identity when include-set changes (that **may** `BuildJobs`).

Done when: uncheck three thumbs, batch/combo/random/single ignore them; drag of an unchecked thumb onto a slot still fills it.

How to verify: App smoke.

## Phase 9 — Color / mono mass include

Touch: Photos pane buttons, Core chroma helper, prefer `ThumbnailItem.Bitmap` pixels.

Four buttons as locked. Do not decode full camera files when a thumb exists.

Done when: Color off unchecks saturated thumbs and leaves gray ones; Mono off does the opposite; faded gray counts as mono.

How to verify: Core chroma test on gray vs red synthetic; App click the four buttons.

## Phase 10 — Canvas size on geometry; Grid 2×2 H landscape

Touch: `LayoutGeometry` (+ canvas fields, bleed clamp), `CollageExporter`, `CollagePreviewRenderer` (scale + edge seal), `ManualStageGeometry.ComputeMetrics(canvasW, canvasH)`, `MainWindow.GetStageMetrics`, tests that used `Constants.Canvas*` as layout bounds.

Pass paths into `Compute` (unused except later Stack 1). `grid-2x2-h` export 4800×3840. Other current layouts 3840×4800. Preview header `Preview`.

Done when: 2×2 H JPEG is 4800×3840; stack-3 still 3840×4800; stage hit-test matches landscape preview.

How to verify: export/geometry tests named in this phase; build App, pick 2×2 H.

## Phase 11 — Stack 1

Touch: catalog, `UiConstants.LayoutOrder` (put Stack 1 first), `Compute` path-driven canvas, `EditableCollage` one slot, CLI help, tests with portrait vs landscape synthetics. Not in combo.

Done when: one included landscape → 4800×3840; one portrait → 3840×4800; batch writes one file per included photo.

How to verify: those tests; App Stack 1.

## Phase 12 — Bleed blend

Touch: `CollageExporter.ComposeCanvas` only (preview already calls it). Keep grow in `LayoutGeometryCalculator`. 16 px full-res ramp. Borderless skips blend.

Done when: stack-2 bleed on is a soft join vs off; not a hard later-tile cut; toggle is quick on one stage.

How to verify: geometry overlap test; App stack-2 toggle.

## Phase 13 — Noise and Orton engine

Touch: small Core apply-after-compose helper; `CollageExporter.BuildCollageImage`; `CollagePreviewRenderer` both renders; thread bools (default false so CLI unchanged).

Done when: flags true change pixels vs false on a synthetic collage; false path matches today’s output for the same slots.

How to verify: one Core test each for noise and Orton vs dry.

## Phase 14 — Noise and Orton controls

Touch: `EditableCollage` flags, `MainWindow` right pane two toggles, preview request, export-from-collage, `ManualUndoStack` (snapshot flags), deck thumb invalidate on toggle. No `BuildJobs`.

Done when: toggle on focused card, stage and that export show it; neighbor card stays clean; undo reverts.

How to verify: App on a filled collage.

## Phase 15 — Audit

Read catalog, `ComboJobSpecs`, scanner, deck focus/rebuild split, geometry canvas, compose bleed, effects after assemble, XAML entities, white `#FFFFFF` on option **text**. Confirm no `grid-1x3-h`, combo four specs, deck click does not `BuildJobs`, 2×2 H landscape, Stack 1 follows orientation.

Done when: gaps fixed here or named as waived to the human.

How to verify: code read + one App launch. No full test suite.

## Order

1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12 → 13 → 14 → 15.

Sequential. One Composer 2.5 (non-fast) implementer per phase. Later phases assume earlier done-whens. Do not start 10 before 3 (catalog canvas flag). Do not start 12 before 10 (canvas on geometry). Do not start 14 before 13. Phase 15 last.

## Estimated duration

- Phase 1: 2–3 agent-hours
- Phase 2: 1–2 agent-hours
- Phase 3: 2–3 agent-hours
- Phase 4: 2–4 agent-hours
- Phase 5: 2–3 agent-hours
- Phase 6: 1–2 agent-hours
- Phase 7: 2–4 agent-hours
- Phase 8: 3–5 agent-hours
- Phase 9: 2–3 agent-hours
- Phase 10: 4–6 agent-hours
- Phase 11: 3–5 agent-hours
- Phase 12: 3–5 agent-hours
- Phase 13: 3–4 agent-hours
- Phase 14: 2–4 agent-hours
- Phase 15: 2–4 agent-hours
