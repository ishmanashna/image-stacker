#!/usr/bin/env python3
"""Export one collage per layout and write SHA-256 hashes (Phase 0 baseline)."""

from __future__ import annotations

import hashlib
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from app.engine.layout_engine import LAYOUT_CONFIG, run_collage_from_paths
from app.io import get_valid_paths


def _fixture_dir() -> Path:
    test_images = ROOT / "TEST IMAGES"
    if not test_images.is_dir():
        raise SystemExit(f"Missing fixture folder: {test_images}")
    return test_images


def _sha256(path: Path) -> str:
    h = hashlib.sha256()
    h.update(path.read_bytes())
    return h.hexdigest()


def main() -> int:
    folder = _fixture_dir()
    out_dir = ROOT / "tests" / "fixtures" / "golden"
    out_dir.mkdir(parents=True, exist_ok=True)
    lines: list[str] = []
    for layout_name, cfg in LAYOUT_CONFIG.items():
        orient = cfg["orientation"]
        n = cfg["num_images"]
        paths = get_valid_paths(folder, orient)
        if len(paths) < n:
            print(f"SKIP {layout_name}: need {n} {orient}, have {len(paths)}")
            continue
        dest = out_dir / f"baseline_{layout_name}.jpg"
        run_collage_from_paths(
            paths[:n],
            layout_name,
            borderless=False,
            color="white",
            output_path=dest,
        )
        digest = _sha256(dest)
        lines.append(f"{digest}  {dest.name}")
        print(f"Wrote {dest}  {digest[:16]}…")

    report = ROOT / "docs" / "BASELINE_HASHES.txt"
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text("\n".join(lines) + ("\n" if lines else ""), encoding="utf-8")
    print(f"Report: {report}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
