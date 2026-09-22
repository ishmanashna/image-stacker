# Architecture

Image Stacker is a Windows collage tool: pick photos, arrange them in fixed Instagram portrait layouts (3840×4800 JPEG, ≤8 MB), and export. The product is .NET 8 with a shared Core library, a WPF app, and a CLI. Image work goes through libvips via NetVips.

## Pieces

| Project | Role |
|---------|------|
| `ImageStacker.Core` | Layouts, folder scan, cover/pan pipeline, JPEG encode, combo/job listing, parallel export, scaled preview render |
| `ImageStacker.Cli` | Unattended export (`--layout`, `--batch`, `--random`, `--combo`, …) |
| `ImageStacker.App` | WPF UI: library thumbs, live stage, editable collages (any mode + blank collage), pick-then-export deck |
| `ImageStacker.Tests` | xUnit coverage for geometry, encode, combinations, pan parity |

```
photos folder
    │
    ▼
ImageScanner / OrientationHelper ──► candidate lists (single / batch / random / combo)
    │
    ├─► App: preview (CollagePreviewRenderer, thumbnail decode)
    │         deck ──► Run: export current or all ticked (each with slot crops)
    │         blank collage ──► one ExportCollage
    │
    └─► Cli: ExportJobRunner (all jobs)
              │
              ▼
         ImagePipeline (cover crop → resize → flip/gray)
              │
              ▼
         3840×4800 canvas → JpegEncoder (Q 92→80→68→65, 4:4:4, ≤8 MB)
```

## Layouts and modes

Layouts live in Core (`LayoutCatalog` / `LayoutGeometryCalculator`): stack-1/2/3/4, grid-1x2-v, grid-1x3-v, grid-2x4, grid-3x3, grid-2x2-v/h. Portrait canvas 3840×4800, landscape 4800×3840 for Grid 2×2 H and Stack 1 landscapes.

Modes: single (first N), batch (non-overlapping chunks), random (`--count` groups), combo (fixed 40-job standard set), blank collage (user-filled slots with pan/flip/gray). Any generated collage can be edited on the stage in other modes too.

GUI combo/batch/random: virtualized deck; Run exports the focused card or all **ticked** cards (default none selected). CLI always writes the full set.

## Image rules (Core)

- Flat folder scan, paths sorted; H/V filters exclude squares; mixed accepts any readable image.
- Cover: crop in source space, then one resize; pan in [-1, 1]. `grid-1x2-v` defaults to opposite-half pans.
- Export decode is full-res via a thread-safe `SourceImageCache` (copy under lock). Preview uses shrink-on-load thumbnails.
- Parallel export: TPL capped at 8 workers, `NetVips.Concurrency = 1` for the batch, then restored.
- PNG/alpha flattened onto white before cover.

## App shell

WPF on Windows. Thumbs and stage bitmaps are built as buffers off-thread, then `WriteableBitmap` on the UI thread. Settings and logs under `%LOCALAPPDATA%\ImageStacker\`. While Run is busy, layout/mode/deck edits are locked.

## Out of scope

Carousel/strip export, non-Windows GUI, matching old Python JPEG bytes.
