# Desktop restack

Replace the Python/Pillow/Tk app with a Windows desktop product in C# (.NET 8, WPF). Image work goes through libvips (NetVips + NetVips.Native.win-x64). The GUI builds a deck of candidates, you pick, then it writes files. The CLI still exports unattended when asked.

This is a rewrite, not a wrapper around Python. Python stays on disk only until the last phase, then it is deleted. Do not treat `run_gui.py` / Tk as a runnable reference — the split left `main_window.py` incomplete. Spec sources: `app/engine/layout_engine.py`, `app/io.py`, `app/preview.py`, and the intact parts of `main_window.py` (modes, manual gestures, settings keys, layout card order).

## Why this shape

Measured on real ~18 MP photos (8-core PC): one stack-3 export is about 8 seconds. Cover-resize and JPEG encode each take ~40%, decode ~19%, pasting tiles onto the canvas ~2%. The 8 MB cap currently encodes the same 3840×4800 image six times. Preview is still a second CPU pipeline (~1.2 s). Multiprocessing helps batch (~3× at eight jobs) but eight stack-3 files still cost ~20 s.

So: native shrink-on-load and a size-aware JPEG for export; never decode 18 MP to show a 900 px stage; do not pay 8 s for collages nobody kept. Skip a GPU compositor (composite is ~2%). WPF plus vips thumbnails is the stage. Display path is vips memory → `WriteableBitmap` (or NetVips.Extensions). Never JPEG-roundtrip a preview frame.

C# not C++: Windows 10, free WPF, NetVips is libvips without a CMake/Qt tree. Publish self-contained win-x64. Include libvips LGPL attribution in `dist/`.

## Product (complete, not a slice)

Instagram portrait collages, canvas 3840×4800, JPEG out, file at most 8 MB. Inputs: `.jpg` `.jpeg` `.png`. EXIF orientation respected. PNG/RGBA/LA/P flattened onto white before cover (same as current `flatten_alpha`). Background color by name or `#RRGGBB`; invalid color → white.

Layouts (same geometry as today):

- stack-2, stack-3: landscape strips, unframed. Spacing 75, margin 0; borderless is 0/0.
- grid-1x2-v: two portrait columns. Default pan is hard left on slot 0 and hard right on slot 1 for preview, manual defaults, CLI, and parallel workers alike (today’s `worker_job` wrongly centers — fix in the port).
- grid-1x3-m: three mixed-orientation cells (any orientation accepted).
- grid-2x4: eight landscape cells, 4 rows × 2 cols.
- grid-3x3: nine landscape cells.
- grid-2x2-v: four portrait cells, framed (spacing 150, margin 150) unless borderless.

Bleed: first row edge-to-edge when not borderless. Cover crop (fill the cell, no letterbox). Manual slots: pan, horizontal flip, grayscale.

Orientation filter: landscape means `w > h`, portrait `h > w` after EXIF; squares match neither H/V filter. Mixed layouts (`grid-1x3-m`) accept any readable image including squares.

Input folder is non-recursive (direct children only). Eligible files are collected and sorted by full path string (same as Python `sorted(Path)`). Single and batch use that order; random shuffles a copy. `--batch --random` together: shuffle once, then take non-overlapping chunks (same as today).

Modes:

- Single: one collage from the first N matching photos. `--count` does not multiply singles.
- Batch: every non-overlapping group of N matching photos (count unused unless also random — see above).
- Random: exactly `--count` groups; reshuffle and refill when the pool runs dry.
- Combo (standard set): 10× grid-2x4 borderless, 10× stack-3 borderless, 10× grid-2x2-v framed, 10× grid-2x2-v borderless, random grouping. Layout and borderless controls are disabled/ignored in combo (each job carries its own borderless). Bleed and color still apply globally. GUI: multi-card deck with ticks (see phase 6). CLI: write all jobs; no half-baked restrict flag.
- Manual: fill every slot, pan/flip/gray, one export.

Library: folder of photos, landscape 3:2 thumb tiles with contain + letterbox (portrait files letterbox inside the tile). Stage: live layout, arrow-key / buttons to walk candidates, “use in manual”. Run progress, open output folder, shortcuts, settings in `%LOCALAPPDATA%\ImageStacker\settings.json`, log `%LOCALAPPDATA%\ImageStacker\logs\app.log`. Color presets: white, black, beige, ivory, gray, lightgray, darkgray, wheat, tan, navy, maroon, plus custom.

CLI flags: folder, `--layout`, `--combo`, `--batch`, `--random`, `--count`, `--output`, `--borderless`, `--bleed`, `--color`. Combination rules match `generate_combinations`. Layout-job filename: `{layout}_{index:03d}_{timestamp}_{rand}.jpg`. Manual: `manual_{timestamp}_{rand}.jpg` (no index).

## Engine rules (all phases)

Cover (normative, crop-in-source then one resize — do not scale-then-crop):

- Clamp pan_x, pan_y to [-1, 1].
- Scale factor `r = max(tw/src_w, th/src_h)`. Required source window is `tw/r` by `th/r`.
- Excess in source: `ex_w = src_w - win_w`, `ex_h = src_h - win_h`.
- Crop origin: `left = round((1+pan_x)*ex_w/2)`, `top = round((1+pan_y)*ex_h/2)`, clamped so the window stays inside the source.
- Extract that window, resize to (tw, th) with a sharp kernel (vips similar/lanczos). JPEG: use shrink-on-load / thumbnail when the cell is much smaller than the file.
- Then optional flip_h and grayscale.

Encode (≤8 MB):

- Build the 3840×4800 RGB canvas once.
- JPEG with chroma 4:4:4 (`subsample_mode = Off` — match Pillow `subsampling=0`; do not rely on vips Q&lt;90 default subsample).
- Encode to a memory buffer. libvips has no target-size API.
- Fixed probe table, max 4 encodes: try Q in order `{92, 80, 68, 65}`. Stop early at the first (highest) Q that fits ≤8 MB. If none fit, write the smallest of the four buffers and log a warning. Floor Q = 65 (match `save_optimized`). Do not unbounded binary-search; do not start at Q=95.

Decode each distinct source path once per export batch (shared thread-safe cache). Orientation from JPEG/EXIF headers when possible, not a full pixel decode for folder scans.

Preview and export share layout math. Preview never uses export cell pixel size. Stage long edge ~900–1200 px via vips thumbnail/shrink.

Parallel export with TPL (degree min(8, CPU, job count)). Inside each worker set `NetVips.NetVips.Concurrency = 1` (or equivalent) so TPL×libvips does not square threads. No Python, no Pillow, no second image stack beside vips.

Solution layout: `src/ImageStacker.Core`, `src/ImageStacker.Cli`, `src/ImageStacker.App` (WPF), `src/ImageStacker.Tests`. Agents do not run git. PlatformTarget x64; Prefer32Bit false. Core, Cli, and App each reference NetVips.Native.win-x64 directly (App must not rely on transitive copy alone). Preview bitmaps: create/freeze WriteableBitmap on the UI thread only.

Core contracts (name in phase 1; finish ownership as noted):

- Layout geometry + color parse + cover/export as above (phase 1).
- `SlotAssignment` (path, pan_x, pan_y, flip_h, grayscale) (phase 1).
- Thread-safe `SourceImageCache` for export batches (phase 1).
- `ListLayoutCandidates(folder, layout, count, batch, random, borderless)` → list of path lists (mirrors `list_layout_combinations`). Fully implemented in phase 2.
- `ListComboSequences(folder)` → list of `(paths, layoutName, borderless)` in Run order (mirrors `list_combo_preview_sequences`). Fully implemented in phase 2. Required for combo preview and the phase 6 deck.
- `RenderPreview(...)` — scaled collage with borderless, bleed, color; optional per-slot pan/flip/gray. Completed in phase 4 (phase 1 may stub).

## Out of scope

Carousel/strip export. macOS/Linux GUI. App stores, paid certs, cloud. GPU/compute shaders. Matching old JPEG bytes/hashes. Keeping PyInstaller or Python entrypoints after cutover.

## Phase 1 — Core library

New .NET 8 class library + Cli host that can write one collage. Port layout config and geometry (spacing, margin, framed, bleed, per-cell sizes/positions). Port color parse and alpha flatten. Flat-folder sorted path scan. NetVips load, EXIF, cover into a cell with the pan math above, flip, grayscale, paste onto 3840×4800, save under 8 MB with the fixed Q probe table. `grid-1x2-v` default pans on the export path used by Cli. Name/stub Core contracts (`RenderPreview` thin; listing APIs named). Package NetVips.Native.win-x64 on Core and Cli; smoke `dotnet publish` Cli so native DLLs resolve before phase 7.

Done when: `dotnet run --project src/ImageStacker.Cli -- <photos> --layout stack-3 --count 1 --output <dir>` writes a 3840×4800 JPEG under 8 MB that looks like a three-strip stack; same for grid-2x4; grid-1x2-v export shows opposite-half crops (not centered). xunit tests: canvas size, 8 MB on a generated canvas, cell rects for every layout × borderless × bleed, pan edge cases for cover math, one PNG-with-alpha smoke. Synthetic large JPEGs in tests when `TEST IMAGES/` is absent.

Verify: Cli on local photos if present; `dotnet test` on the new test project only. Do not run a giant suite.

## Phase 2 — Jobs and CLI

Combination building: single = first N only (ignore count); batch = non-overlapping chunks; random = count groups with refill; combo = COMBO_JOB_SPECS. Fully implement `ListLayoutCandidates` and `ListComboSequences` (same sequences and order as export jobs). Parallel workers call the same export path as Cli (including `grid-1x2-v` default pans), with SourceImageCache and per-worker vips concurrency 1. CLI accepts every flag above; prints skip/finish clearly. Filenames with zero-padded deck/job index.

Done when: `--combo`, `--batch`, `--random --count`, `--borderless`, `--bleed`, `--color` work without Python. Combo writes up to 40 files when the folder has enough H and V shots. One bad file does not abort the rest. Listing APIs match job order. Test: combination counts for single/batch/random/combo (not pixel hashes).

Verify: one combo or batch on a folder with enough images; confirm file count.

## Phase 3 — WPF shell

ImageStacker.App: windowed WPF, Windows 10. Reference NetVips.Native.win-x64 directly. Left: library thumbs (cap, batched load, vips thumb → WriteableBitmap on UI thread). Layout cards in current order. Mode, count, borderless, bleed, color presets + custom. In combo mode, disable layout cards and borderless (bleed/color stay). Preview bar / arrows may exist in chrome but stay inactive until phase 4. Input/output browse. Persist settings on close (same keys as today’s JSON). Log under LocalAppData. Shortcuts dialog. Status + run progress. Open output folder after success. Run calls Core only (never Python). Until phase 6, multi-file modes may still write everything — that is an intentional interim, not the final product; do not polish it as the UX goal.

Done when: pick folder, see thumbs, pick layout, set color/bleed, run single, get a JPEG; settings survive restart; UI stays responsive during export.

Verify: launch, one single export, reopen and see last folders.

## Phase 4 — Live stage (auto)

Complete `RenderPreview` (borderless, bleed, color, optional slot transforms). Stage shows the current candidate at preview scale. Debounce folder/layout/color changes. Prev/next walks `ListLayoutCandidates` / `ListComboSequences` (combo cards carry layout + borderless). Empty-state when not enough photos of the required orientation. “Use in manual” copies paths into `SlotAssignment`s; for `grid-1x2-v` set slot 0 `pan_x = -1`, slot 1 `pan_x = +1`, `pan_y = 0` (match stage/export defaults). Manual editing is phase 5; slots must already be filled. Warm preview after arrows should beat a full export by a clear margin (vips thumbs, not the old ~1.2 s Python path).

Done when: layout/arrow changes update the stage without writing 3840×4800 and without decoding sources at export size; “Use in manual” on grid-1x2-v keeps opposite-half pans.

Verify: browse several combo/single candidates; resize window; slot count matches layout.

## Phase 5 — Manual

All slots for the current layout. Drag thumb onto a slot or click to fill next empty. Drag on a filled slot to pan (commit on release; low-res or cached cell while dragging, full preview settle on release). Double-click flip; Shift+double-click grayscale. Ctrl+drag swap. Right-click clear. Ctrl+Z / Ctrl+Y, cap 50. Run writes one JPEG with those pans/flips/grays. Stage from preview mips; export full-res with the same pan coordinates.

Done when: every manual shortcut works; export matches stage (crop side, flip, gray); undo restores a clear/pan.

Verify: fill stack-3, pan middle, flip one, export, open the JPEG.

## Phase 6 — Pick then export

Replace “write everything” for combo, and for batch/random when more than one file would be written. UI: virtualized multi-card deck (not only the single stage). Each card shows a preview bitmap (lazy `RenderPreview` on scroll/focus — do not eagerly bake all 40), a tick, select-all / select-none. Stage arrows can stay as a detail view of the focused card. Default: nothing ticked; select-all is visible so one-click full export remains. Run writes only ticked jobs; progress counts ticks only. Zero ticks → write nothing (status explains). Ticked jobs keep their deck index in `{layout}_{index:03d}_…` (same index as a full-deck run — do not renumber 001..N by tick order). CLI unchanged (full combo/batch). Single and manual still one file.

Done when: tick three combo cards, run, exactly three JPEGs from that run with original deck indices; zero ticks writes zero files.

Verify: as above, and confirm select-all then run writes the full deck.

## Phase 7 — Cutover

Delete the Python product: `app/`, `run_gui.py`, `script.py`, `ImageStacker.spec`, `build_windows.ps1`, `requirements.txt`, `requirements-dev.txt`, Python `tests/`, Python-only `scripts/`, and `docs/BASELINE_HASHES.txt` if it only served Python hashes. Keep `TEST IMAGES/` gitignored. Replace README with C# run / test / publish. Replace `.github/workflows/tests.yml` with a Windows `dotnet test` + publish-smoke workflow. Update or remove `.cursor/rules/run-with-python.mdc` so post-cutover guidance is not Python. Publish App and Cli self-contained win-x64 into `dist/` (gitignored) with native vips DLLs and LGPL license files. Optional: a thin C# microbench under tests or `src` so encode/resize regressions stay measurable after deleting `app/perf/`.

Done when: clean clone (no Python) builds and tests on Windows; README and CI have no Python app entrypoints; no leftover product `.py`. Dist runs and exports once.

Verify: `dotnet test`; publish App; launch exe; one export. No Python fallback.

## Estimated duration

- Phase 1: 8–12 agent-hours
- Phase 2: 5–8 agent-hours
- Phase 3: 10–16 agent-hours
- Phase 4: 8–12 agent-hours
- Phase 5: 10–16 agent-hours
- Phase 6: 8–12 agent-hours
- Phase 7: 5–9 agent-hours
