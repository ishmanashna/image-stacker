# Layouts and UI polish

One-sentence outcome: Image Stacker gains the missing 2x2 horizontal and 4-stack layouts, honest Row 1x3 H/V modes, readable dark UI, a fuller photo deck, reliable borderless, and bleed that actually softens seams between photos.

## Scope

In:
- New layout `grid-2x2-h`: four horizontal photos in a 2x2, framed like the existing vertical 2x2.
- New layout `stack-4`: four horizontals stacked top-to-bottom (same rules as stack-2 / stack-3).
- Replace the lying `grid-1x3-m` "H or V" card with two real layouts: `grid-1x3-h` and `grid-1x3-v`. Remove Mixed-as-a-fake-toggle.
- Bleed means adjacent cells overlap a bit so photos meet soft, not a hard gutter. Applies to every multi-cell layout including stacks. Drop the old "top row edge-to-edge only" behavior and rename the checkbox to match.
- Borderless removes gutters/frame the same way in preview and export for every layout that supports it.
- Default cover crop for horizontal cells biases toward the upper goldilocks (about two-thirds up), same idea as the other cuts.
- Right sidebar and Run / Shortcuts / Open output buttons: readable contrast on the dark theme.
- Photo deck (scroll thumbs) uses the panel width properly (more columns / wrapping that fills the space).

Out of scope:
- New export canvas size or Instagram size changes.
- New run modes (batch/random/combo logic stays).
- Rewriting the editable-collage system beyond what these layout/UI fixes force.
- GitHub, remotes, or packaging/installers.

## Decisions (locked for implementers)

- Layout ids are first-class catalog entries. No runtime "orientation enum" bolted onto Mixed.
- `grid-1x3-m` goes away; callers and UI use `grid-1x3-h` / `grid-1x3-v` only (replace, do not leave a Mixed shim).
- `grid-2x2-h` is Framed like `grid-2x2-v`. `stack-4` is unframed like the other stacks.
- Bleed is seam overlap in geometry (shared edge inset), not a first-row full-bleed hack. Borderless wins over bleed (bleed ignored when borderless).
- Goldilocks default is a small upward pan on cover crops for horizontal-oriented cells; vertical-oriented cells keep the existing opposite-halves / center behavior already used by split layouts.
- Preview and export must share the same geometry + crop inputs so what you see is what you get.

## Phases

### Phase 1 — Catalog and geometry for new layouts

Add `stack-4`, `grid-2x2-h`, `grid-1x3-h`, `grid-1x3-v` to the layout catalog and geometry path. Remove `grid-1x3-m`. Cell counts, row/col math, framed vs unframed spacing, and orientation filters must be correct. Combo/job helpers that listed the old Mixed id must list the new ones (or stay intentionally on the old set only if combo still targets a fixed menu — then update that menu deliberately, no silent Mixed).

Done when: geometry unit tests cover the four layouts (cell count, non-overlap without bleed, framed margins on 2x2-h, stack-4 fills canvas height). Unknown-layout still throws. No remaining code path requires `grid-1x3-m`.

How to verify: targeted Core geometry/catalog tests only (not the full suite).

### Phase 2 — Borderless reliability and real bleed

Make borderless a single clear path: spacing and margin zero in geometry, used by both preview and export (including editable/manual collage if it has its own copy of the flag). Fix any place that ignores the checkbox, applies spacing twice, or preview/export diverge.

Replace bleed implementation: with bleed on and borderless off, neighboring cells overlap by a fixed pixel amount along shared edges (horizontal stacks overlap on the horizontal seam; grids overlap on both axes as needed). Update the checkbox label/tooltip to say it softens seams. Stack-2 must visibly change with bleed on.

Done when: for stack-2, stack-3, and grid-2x2-v, borderless preview matches export; bleed on vs off changes geometry tests; bleed + borderless equals borderless.

How to verify: geometry tests for bleed/borderless matrices on stack-2 and one grid; one export smoke comparing borderless preview bytes policy if tests already do image compares — otherwise geometry + a small exporter assertion is enough.

### Phase 3 — Goldilocks default crop

Default pan for horizontal cover crops biases up so the kept band sits around the upper two-thirds of the source (not dead-center). Vertical split layouts keep their existing half-pan behavior. Editable slots that already store pan values must not be overwritten on load; default only applies when no slot pan is set.

Done when: a unit or pipeline test shows default H crop is upward-biased vs center; explicit slot pan still wins.

How to verify: targeted imaging/pipeline tests.

### Phase 4 — WPF wiring and dark UI

Register the new layouts in layout button order and card titles/subtitles. Row 1x3 appears as two cards (H and V), not one. Sidebar labels, radios, checkboxes, and the Run / Shortcuts / Open output controls get enough contrast to read on the dark background. Photo thumb deck WrapPanel fills the available width (raise effective columns / tile sizing so empty black band goes away on a normal window size).

Done when: cold run of the app shows the new cards, Row H and Row V both selectable with matching preview orientation, sidebar and Run row readable, thumbs packing across the photos pane.

How to verify: build the app, launch once, visual check (orchestrator or human). No full test suite from this phase.

### Phase 5 — Audit

Adversarial pass after phases 1–4: read catalog, geometry, exporter, preview, MainWindow wiring. Confirm no Mixed leftovers, no second bleed path, no preview/export split, no low-contrast regressions on the controls touched. Spot-check stack-4 and grid-2x2-h with real photos from a sample folder if present.

Done when: short audit note in the plan folder or chat listing any gaps; gaps fixed or explicitly waived by the human.

How to verify: code read + one manual launch, not a full suite.

## Order

1 → 2 → 3 → 4 → 5. One implementer agent per phase, sequential. Phase 5 only after 1–4 are in.

## Estimated duration

- Phase 1 — catalog + geometry: 1–2 agent-hours
- Phase 2 — borderless + bleed: 2–3 agent-hours
- Phase 3 — goldilocks crop default: 0.5–1.5 agent-hours
- Phase 4 — WPF + contrast + deck: 1.5–3 agent-hours
- Phase 5 — audit: 0.5–1 agent-hour
