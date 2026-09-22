# Image Stacker

Windows desktop and CLI tool for Instagram-style **layout collages** (stack, grid, combo pack, editable slot crops).

Exports portrait collages at **3840×4800** with JPEG output capped at **8 MB** (Instagram upload limit). Built with **.NET 8**, **WPF**, and **libvips** (NetVips).

## Requirements

- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (build and run from source)
- **Windows 10+** for the WPF desktop app (`ImageStacker.App`)
- The CLI (`ImageStacker.Cli`) targets **win-x64** and bundles native libvips when published self-contained

## Quick start

From the repository root:

```powershell
# Desktop app (WPF)
dotnet run --project src/ImageStacker.App

# CLI (help)
dotnet run --project src/ImageStacker.Cli -- --help
```

Point the app at a folder of `.jpg` / `.jpeg` / `.png` images, pick a layout, and export. In combo/batch/random the GUI shows a deck of candidates — edit any card on the stage, tick what you want, then Run (CLI still writes the full set).

### Editing

You can edit any collage on the stage: assign photos from the library, pan, flip, swap slots (Ctrl+drag), clear, undo/redo. Generated single/batch/random/combo cards keep their edits while you browse the deck. **Blank collage** mode starts an empty layout with the same tools.

When a deck is visible, Run asks **export current** (focused card) or **export all** ticked cards. Any photo can go in any slot — the app cover-crops to the cell. Auto batch/random/combo still only *build* from photos that match each layout’s orientation.

### Entry points

| What | Command |
|------|---------|
| GUI (dev) | `dotnet run --project src/ImageStacker.App` |
| CLI | `dotnet run --project src/ImageStacker.Cli -- <folder> --output <dir> [options]` |
| Tests | `dotnet test src/ImageStacker.Tests -c Release` |

## CLI

```powershell
dotnet run --project src/ImageStacker.Cli -- <folder> --output <dir> [options]
```

**Examples:**

```powershell
dotnet run --project src/ImageStacker.Cli -- .\photos --layout stack-3 --output output
dotnet run --project src/ImageStacker.Cli -- .\photos --layout stack-3 --random --count 5 --output output
dotnet run --project src/ImageStacker.Cli -- .\photos --combo --output output
dotnet run --project src/ImageStacker.Cli -- .\photos --layout grid-2x4 --batch --borderless --bleed --color beige --output output
```

**Layouts** (`--layout`):

| Name | Slots | Notes |
|------|-------|-------|
| `stack-2` | 2 | Horizontal strips |
| `stack-3` | 3 | Horizontal strips |
| `stack-4` | 4 | Horizontal strips |
| `grid-1x2-v` | 2 | Two portrait columns |
| `grid-1x3-h` | 3 | Three landscape cells |
| `grid-1x3-v` | 3 | Three portrait cells |
| `grid-2x4` | 8 | 4×2 grid |
| `grid-3x3` | 9 | 3×3 grid |
| `grid-2x2-v` | 4 | Framed 2×2 (portrait slots) |
| `grid-2x2-h` | 4 | Framed 2×2 (landscape slots) |

**Flags:**

| Flag | Description |
|------|-------------|
| `--combo` | Standard set: 10× grid-2x4 (NB), 10× stack-3 (NB), 10× grid-2x2-v framed, 10× grid-2x2-v borderless |
| `--count N` | Number of groups for `--random` only (ignored for plain single/batch) |
| `--batch` | Non-overlapping groups of N photos (sorted path order) |
| `--random` | Random groups; use with `--count` |
| `--output DIR` | Output folder (required) |
| `--borderless` | Remove borders / frames |
| `--bleed` | Top row edge-to-edge (editorial style) |
| `--color NAME\|#HEX` | Background color (default `white`) |

Run with `--help` for the full list.

## Tests

```powershell
dotnet test src/ImageStacker.Tests -c Release
```

Tests use a local `TEST IMAGES/` folder when present; otherwise they generate temporary synthetic images (no fixture photos are required in the repo).

## Settings and logs

On Windows, the desktop app stores:

- **Settings:** `%LOCALAPPDATA%\ImageStacker\settings.json`
- **Logs:** `%LOCALAPPDATA%\ImageStacker\logs\app.log`

## Project layout

```
src/
  ImageStacker.Core/    # Layout engine, export, preview
  ImageStacker.Cli/     # Command-line export
  ImageStacker.App/     # WPF desktop UI
  ImageStacker.Tests/   # xUnit tests
ARCHITECTURE.md         # How the pieces fit together
docs/
  DESKTOP_RESTACK_PLAN.md
  POST_RESTACK_FIXES_PLAN.md
```

## Publish (self-contained win-x64)

Publish outputs go to `dist/` (gitignored). Native libvips DLLs (`libvips-42.dll`, `NetVips.dll`) are copied next to the executable.

**App:**

```powershell
dotnet publish src/ImageStacker.App -c Release -r win-x64 --self-contained true -o dist/app
```

**CLI:**

```powershell
dotnet publish src/ImageStacker.Cli -c Release -r win-x64 --self-contained true -o dist/cli
```

**Both (PowerShell helper):**

```powershell
.\scripts\publish.ps1
```

Published builds include `THIRD_PARTY_NOTICES.txt` with libvips LGPL attribution.

## License

MIT — see [LICENSE](LICENSE).

Third-party native dependency: [libvips](https://www.libvips.org/) (LGPL). See `THIRD_PARTY_NOTICES.txt` in published `dist/` output.
