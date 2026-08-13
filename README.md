# Image Stacker

Desktop and CLI tool for Instagram-style **layout collages** (stack, grid, combo pack, manual slot editing).

Exports portrait collages at **3840×4800** with JPEG output capped at **8 MB** (Instagram upload limit). This repo is the stacker-only product split from a legacy monolith; it does **not** include carousel strip export.

## Requirements

- **Python 3.10+**
- **Pillow** (`pip install -r requirements.txt`)
- **tkinter** for the GUI (included with the standard Windows/macOS Python installers; on many Linux distros install `python3-tk`)

## Quick start

**Windows (PowerShell):**

```powershell
python -m venv .venv
.venv\Scripts\pip install -r requirements.txt
python run_gui.py
```

**macOS / Linux:**

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
python run_gui.py
```

Point the GUI at any folder of `.jpg` / `.jpeg` / `.png` images, pick a layout, and export.

### Entry points

| What | Command |
|------|---------|
| GUI (dev) | `python run_gui.py` or `python -m app.gui.main` |
| CLI | `python script.py …` or `python -m app.cli …` |
| Perf harness | `python -m app.perf` (writes `perf_reports/`) |

## CLI

```powershell
python script.py <folder> [options]
```

**Examples:**

```powershell
python script.py .\photos --layout stack-3 --count 2 --output output
python script.py .\photos --combo --output output
python script.py .\photos --layout grid-2x4 --batch --borderless --bleed --color beige --output output
```

**Layouts** (`--layout`):

| Name | Slots | Notes |
|------|-------|-------|
| `stack-2` | 2 | Horizontal strips |
| `stack-3` | 3 | Horizontal strips |
| `grid-1x2-v` | 2 | Two portrait columns |
| `grid-1x3-m` | 3 | Mixed orientation row |
| `grid-2x4` | 8 | 4×2 grid |
| `grid-3x3` | 9 | 3×3 grid |
| `grid-2x2-v` | 4 | Framed 2×2 (portrait slots) |

**Flags:**

| Flag | Description |
|------|-------------|
| `--combo` | Standard set: 10× grid-2x4 (NB), 10× stack-3 (NB), 10× grid-2x2-v framed, 10× grid-2x2-v borderless |
| `--count N` | Number of collages to generate (default `1`) |
| `--batch` | Use all available images in combinations |
| `--random` | Shuffle image order |
| `--output DIR` | Output folder (default `output`) |
| `--borderless` | Remove borders / frames |
| `--bleed` | Top row edge-to-edge (editorial style) |
| `--color NAME\|#HEX` | Background color (default `white`) |

Run `python script.py --help` for the full list.

## Tests

Smoke tests export one collage per layout. They use a local `TEST IMAGES/` folder if present; otherwise they generate temporary synthetic images (no fixture photos are required in the repo).

```powershell
.venv\Scripts\python.exe -m unittest tests.test_layout_smoke -v
```

```bash
python -m unittest tests.test_layout_smoke -v
```

Regression baseline hashes live in `docs/BASELINE_HASHES.txt` (regenerate with `scripts/generate_baseline_hashes.py` when you have a local `TEST IMAGES/` folder).

## Logs and performance

- **GUI logs:** `%LOCALAPPDATA%\ImageStacker\logs\app.log` on Windows; `~/ImageStacker/logs/app.log` elsewhere (`LOCALAPPDATA` if set, otherwise your home directory).
- **Perf tracing:** set `IMAGE_STACKER_PERF=1` or pass `--perf` / `--perf-out DIR` to the GUI entry.

## Project layout

```
app/
  cli.py              # CLI implementation
  engine/             # Layout engine and batch jobs
  gui/                # Tkinter desktop UI
  io.py               # Image I/O, colors, JPEG save
  preview.py          # Preview rendering
  perf/               # Optional performance harness
run_gui.py            # GUI launcher
script.py             # CLI shim
tests/                # Smoke tests
docs/                 # Baseline hashes
scripts/              # Dev utilities
```

## Build (optional)

Windows one-file executable via PyInstaller (not required for normal use):

```powershell
pip install -r requirements-dev.txt
.\build_windows.ps1
```

Produces `dist\ImageStacker.exe`. Alternatively: `pyinstaller ImageStacker.spec`.

## License

MIT — see [LICENSE](LICENSE).
