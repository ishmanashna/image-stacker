# Post-restack fixes

Close gaps found by implementation and system audits after the desktop restack. Core product works (41 tests green); these are the items that still break the written rules or will bite users.

## Scope

In scope: deck tick persistence, WriteableBitmap UI-thread rule, JPEG oversize warning, parallel NetVips safety, manual pan throttling, manual orientation validation, stronger pan/JPEG tests, CI App publish smoke.

Out of scope: shrink-on-load for export (perf polish), header-only EXIF scan, C# microbench, redesign of layout math.

## Phase 1 — Deck and preview threading

Preserve deck ticks and focus across rebuilds that are not a real candidate-list change (color, bleed, folder refresh after export). Only rebuild card identities when folder/mode/layout/count/borderless/combo sequences change. On soft refresh, update color/bleed used for lazy previews without clearing `IsSelected` or resetting focus to 0.

Move `WriteableBitmap` creation in `DeckService.QueuePreview` onto the UI dispatcher (same pattern as thumbs and stage). Cancel in-flight deck preview work so orphaned cards do not get bitmaps.

Done when: tick three combo cards, change bleed/color, ticks remain; after a successful Run, ticks remain unless the candidate list itself changed; deck bitmaps are only constructed on the UI thread.

Verify: manual App combo flow as above; code review of `DeckService` / `MainWindow` refresh paths.

## Phase 2 — Encode warning and parallel safety

When all JPEG probes exceed 8 MB, write the smallest buffer and log a clear warning (CLI stderr and App file log). Add a Core hook or `ILogger`/callback if Core has no logger yet — CLI and App must surface it.

Harden shared `SourceImageCache` under parallel export: either lock around read+Copy of a cached image, or store bytes/path and decode per worker. Document the chosen rule in code. Restore `NetVips.Concurrency` after a batch (or set it once at process start and keep preview gated while export runs — pick one coherent policy).

Reduce filename collision risk under parallel jobs (include job index already present; add ticks or a unique suffix if still racing).

Done when: forced oversize path logs a warning; parallel combo export of many jobs does not crash; concurrency is not left accidentally stuck in a bad state after Run.

Verify: unit or integration test for oversize warning; stress combo export on synthetic photos.

## Phase 3 — Manual UX hardening

Throttle manual pan live preview (latest-wins; do not stack full collage `Task.Run` per mouse move). On assign or Run validate, reject wrong-orientation photos with a clear status/dialog (not a generic export exception). Optionally disable layout/mode/deck edits while `_busy` during Run.

Done when: dragging to pan stays smooth on stack-3; assigning a landscape into a portrait-only layout fails early with readable copy; Run busy state does not let the user retick the deck mid-export.

Verify: App manual smoke; one wrong-orientation assign attempt.

## Phase 4 — Tests and CI

Strengthen tests: `grid-1x2-v` left vs right crop origins differ; preview vs export pan consistency at ±1 for at least one layout; JPEG probe floor / warning path; combination of parallel export filename uniqueness if feasible.

CI: add self-contained publish smoke for `ImageStacker.App` (win-x64) asserting `libvips*.dll` and `NetVips.dll` beside the exe (keep existing Cli smoke).

Done when: new tests fail if opposite-half pans regress; CI publishes App and checks native DLLs.

Verify: `dotnet test`; inspect workflow YAML.

## Estimated duration

- Phase 1: 3–5 agent-hours
- Phase 2: 4–6 agent-hours
- Phase 3: 3–5 agent-hours
- Phase 4: 3–5 agent-hours
