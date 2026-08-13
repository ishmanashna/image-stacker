"""Drive the real Tk UI through scripted actions; writes perf_reports on exit."""

from __future__ import annotations

import argparse
import os
import tempfile
import time
from pathlib import Path

from PIL import Image

from app.engine.layout_engine import IMAGE_EXTENSIONS, get_layout_geometry, get_valid_paths


def _project_root() -> Path:
    return Path(__file__).resolve().parent.parent.parent


def _count_image_files(folder: Path) -> int:
    n = 0
    try:
        for p in folder.iterdir():
            if p.is_file() and p.suffix.lower() in IMAGE_EXTENSIONS:
                n += 1
    except OSError:
        pass
    return n


def _dir_has_images(folder: Path) -> bool:
    return _count_image_files(folder) > 0


def _resolve_photo_dir(explicit: str | None) -> tuple[Path, str]:
    """Return (absolute folder, label for logging)."""
    if explicit:
        p = Path(explicit).expanduser().resolve()
        if not p.is_dir():
            raise SystemExit(f"--photo-dir is not a directory: {p}")
        return p, str(p)

    root = _project_root()
    marker = root / "TEST IMAGES"
    if marker.is_dir():
        if _dir_has_images(marker):
            return marker.resolve(), "TEST IMAGES"
        for sub in sorted(marker.iterdir()):
            if sub.is_dir() and _dir_has_images(sub):
                return sub.resolve(), f"TEST IMAGES/{sub.name}"

    tmp = Path(tempfile.mkdtemp(prefix="ilg_perf_"))
    fixture = tmp / "photos"
    _write_fixture_images(fixture)
    return fixture.resolve(), f"synthetic (empty TEST IMAGES): {fixture}"


def _write_fixture_images(folder: Path) -> None:
    folder.mkdir(parents=True, exist_ok=True)
    for i in range(28):
        Image.new("RGB", (960, 640), ((i * 9) % 255, 80, 120)).save(folder / f"h_{i:03d}.png")
    for i in range(6):
        Image.new("RGB", (640, 960), (60, (i * 40) % 255, 180)).save(folder / f"v_{i:03d}.png")


def _default_settle_ms(photo_dir: Path, user_settle: int | None) -> int:
    if user_settle is not None:
        return user_settle
    n = _count_image_files(photo_dir)
    return max(12_000, min(120_000, 12_000 + n * 180))


def run_harness(*, settle_ms: int | None = None, photo_dir: str | None = None) -> Path:
    from app.perf.tracker import enable, reset, write_report

    photo_path, photo_label = _resolve_photo_dir(photo_dir)
    settle = _default_settle_ms(photo_path, settle_ms)
    n_files = _count_image_files(photo_path)

    out_dir = Path.cwd() / "perf_reports"
    enable(out_dir=out_dir)
    reset()

    import tkinter as tk

    from app.gui.main_window import MainWindow, ManualSlotFill

    print(
        f"[perf harness] folder={photo_label!r} ({n_files} image files) settle_ms={settle}",
        flush=True,
    )

    root = tk.Tk()
    root.geometry("1200x780")
    mw = MainWindow(root)

    def step_folder() -> None:
        mw.folder_var.set(str(photo_path))
        mw._schedule_folder_preview()

    def step_combo() -> None:
        mw.mode_var.set("combo")
        mw._on_mode_change()

    def step_random_grid() -> None:
        mw.mode_var.set("random")
        mw._on_mode_change()
        mw.count_var.set(4)
        mw.layout_var.set("grid-2x4")
        mw._on_layout_changed()

    def step_batch() -> None:
        mw.mode_var.set("batch")
        mw._on_mode_change()
        mw.layout_var.set("stack-3")
        mw._on_layout_changed()

    def step_layout_via_card() -> None:
        """Exercise layout grid button path (not only StringVar + _on_layout_changed)."""
        mw.mode_var.set("single")
        mw._on_mode_change()
        mw._select_layout("grid-3x3")

    def step_manual_fill() -> None:
        mw.mode_var.set("manual")
        mw._on_mode_change()
        mw.layout_var.set("stack-3")
        mw._on_layout_changed()
        paths = get_valid_paths(photo_path, "horizontal")[:3]
        for i, p in enumerate(paths):
            if i < len(mw._slot_assignments):
                mw._slot_assignments[i] = ManualSlotFill(p)
        mw._paint_stage()
        mw._schedule_preview()

    def step_simulated_thumb_drops() -> None:
        """Synthetic ButtonRelease over the stage — same code path as drag-release onto preview."""
        mw.root.update_idletasks()
        paths = get_valid_paths(photo_path, "horizontal")
        if not paths:
            return
        mw.mode_var.set("manual")
        mw._on_mode_change()
        mw.layout_var.set("stack-3")
        mw._on_layout_changed()
        mw.root.update_idletasks()
        m = mw._stage_metrics()
        if m is None:
            return
        scale, ox, oy = m
        geom = get_layout_geometry(mw.layout_var.get(), mw.borderless_var.get())
        rects = mw._slot_pixel_rects(scale, ox, oy, geom)
        wx = mw.stage.winfo_rootx()
        wy = mw.stage.winfo_rooty()
        for slot in (0, min(2, len(rects) - 1)):
            x0, y0, x1, y1 = rects[slot]
            cx = (x0 + x1) // 2
            cy = (y0 + y1) // 2
            ev = tk.Event()
            ev.x_root = wx + cx
            ev.y_root = wy + cy
            path = paths[min(slot + 1, len(paths) - 1)]
            mw._thumb_release(ev, path)

    def step_resize_jitter() -> None:
        for i, (w, h) in enumerate([(1180, 760), (1240, 800), (1100, 720)]):
            root.after(i * 60, lambda ww=w, hh=h: root.geometry(f"{ww}x{hh}"))

    def step_borderless_toggle() -> None:
        mw.borderless_var.set(not mw.borderless_var.get())
        mw._schedule_preview()

    def step_single_preview_worker() -> None:
        mw.mode_var.set("single")
        mw._on_mode_change()
        mw.layout_var.set("stack-3")
        mw._on_layout_changed()

    # Timeline: folder → modes/layouts → layout card → manual + thumbs → synthetic drops → chrome → preview worker.
    t_drops = max(8_000, settle // 2)
    t_border = max(t_drops + 600, settle - 8_000)
    t_resize = max(t_border + 400, settle - 7_500)
    t_single = max(t_resize + 500, settle - 4_000)

    root.after(80, step_folder)
    root.after(900, step_combo)
    root.after(1800, step_random_grid)
    root.after(2600, step_batch)
    root.after(3200, step_layout_via_card)
    root.after(4000, step_manual_fill)
    root.after(t_drops, step_simulated_thumb_drops)
    root.after(t_border, step_borderless_toggle)
    root.after(t_resize, step_resize_jitter)
    root.after(t_single, step_single_preview_worker)

    t0 = time.perf_counter()

    def finish() -> None:
        # Large folders: last single-mode preview thread may still be inside ``render_worker``.
        time.sleep(1.2 + min(2.0, n_files * 0.015))
        wall = time.perf_counter() - t0
        from app.perf.tracker import record_since

        record_since("harness.session_wall", t0)
        p = write_report(label="harness")
        print(f"[perf harness] wall {wall:.2f}s — report: {p}", flush=True)
        root.destroy()

    root.after(settle, finish)
    root.mainloop()
    return out_dir


def main(argv: list[str] | None = None) -> int:
    os.environ["IMAGE_STACKER_PERF"] = "1"
    p = argparse.ArgumentParser(description="Automated GUI perf harness → perf_reports/")
    p.add_argument(
        "--settle-ms",
        type=int,
        default=None,
        help="Ms before writing report (default scales with image count in folder).",
    )
    p.add_argument(
        "--photo-dir",
        default=None,
        help="Input folder with JPG/PNG. Default: project 'TEST IMAGES' or first subfolder with images, else synthetic.",
    )
    args = p.parse_args(argv)
    run_harness(settle_ms=args.settle_ms, photo_dir=args.photo_dir)
    return 0
