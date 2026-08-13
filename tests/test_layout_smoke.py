"""Smoke: one collage export per registered layout."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from PIL import Image

from app.engine.layout_engine import LAYOUT_CONFIG, run_collage_from_paths, run_layout_job
from app.io import MAX_OUTPUT_JPEG_BYTES, get_valid_paths


def _fixture_dir() -> Path:
    root = Path(__file__).resolve().parent.parent
    test_images = root / "TEST IMAGES"
    if test_images.is_dir():
        return test_images
    tmp = Path(tempfile.mkdtemp(prefix="image_stacker_smoke_"))
    for i in range(12):
        Image.new("RGB", (1200, 800), ((i * 17) % 255, 90, 140)).save(tmp / f"h_{i:03d}.jpg")
    for i in range(6):
        Image.new("RGB", (800, 1200), (70, (i * 35) % 255, 160)).save(tmp / f"v_{i:03d}.jpg")
    return tmp


class TestLayoutExportSmoke(unittest.TestCase):
    def test_one_export_per_layout_key(self) -> None:
        folder = _fixture_dir()
        out = Path(tempfile.mkdtemp(prefix="image_stacker_out_"))
        for layout_name, cfg in LAYOUT_CONFIG.items():
            with self.subTest(layout=layout_name):
                orient = cfg["orientation"]
                n = cfg["num_images"]
                paths = get_valid_paths(folder, orient)
                self.assertGreaterEqual(
                    len(paths),
                    n,
                    f"Need {n} {orient} images for {layout_name}; folder {folder}",
                )
                dest = out / f"smoke_{layout_name}.jpg"
                run_collage_from_paths(
                    paths[:n],
                    layout_name,
                    borderless=False,
                    color="white",
                    output_path=dest,
                )
                self.assertTrue(dest.is_file(), dest)
                self.assertGreater(dest.stat().st_size, 0)
                self.assertLessEqual(
                    dest.stat().st_size,
                    MAX_OUTPUT_JPEG_BYTES,
                    f"{layout_name} exceeds {MAX_OUTPUT_JPEG_BYTES} bytes",
                )

    def test_run_layout_job_stack3(self) -> None:
        folder = _fixture_dir()
        out = Path(tempfile.mkdtemp(prefix="image_stacker_job_"))
        run_layout_job(
            str(folder),
            str(out),
            "stack-3",
            count=1,
            batch=False,
            is_random=False,
            borderless=True,
            color="white",
        )
        jpgs = list(out.glob("*.jpg"))
        self.assertGreaterEqual(len(jpgs), 1)
        for p in jpgs:
            self.assertLessEqual(p.stat().st_size, MAX_OUTPUT_JPEG_BYTES, p.name)


if __name__ == "__main__":
    unittest.main()
