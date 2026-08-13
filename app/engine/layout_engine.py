"""Core layout and image pipeline (extracted from legacy script.py)."""

from __future__ import annotations

import logging
import multiprocessing
import random
import threading
from datetime import datetime
from collections import OrderedDict
from functools import lru_cache, partial
from collections.abc import Callable
from pathlib import Path
from typing import Any, Sequence, Tuple

from PIL import Image, ImageOps

from app.io import (
    IMAGE_EXTENSIONS,
    MAX_FILE_SIZE,
    MAX_OUTPUT_JPEG_BYTES,
    fix_orientation,
    flatten_alpha,
    get_valid_paths,
    parse_color,
    save_optimized,
)

logger = logging.getLogger(__name__)

CANVAS_WIDTH = 3840
CANVAS_HEIGHT = 4800

LAYOUT_CONFIG: dict[str, dict[str, Any]] = {
    "stack-2": {"num_images": 2, "rows": 2, "cols": 1, "orientation": "horizontal", "framed": False},
    "stack-3": {"num_images": 3, "rows": 3, "cols": 1, "orientation": "horizontal", "framed": False},
    # Two portrait slots side by side (canvas split down the middle).
    "grid-1x2-v": {"num_images": 2, "rows": 1, "cols": 2, "orientation": "vertical", "framed": False},
    "grid-1x3-m": {"num_images": 3, "rows": 1, "cols": 3, "orientation": "mixed", "framed": False},
    "grid-2x4": {"num_images": 8, "rows": 4, "cols": 2, "orientation": "horizontal", "framed": False},
    "grid-3x3": {"num_images": 9, "rows": 3, "cols": 3, "orientation": "horizontal", "framed": False},
    "grid-2x2-v": {"num_images": 4, "rows": 2, "cols": 2, "orientation": "vertical", "framed": True},
}


def _cover_resize_and_crop_panned(
    img: Image.Image,
    target_width: int,
    target_height: int,
    pan_x: float,
    pan_y: float,
) -> Image.Image:
    """Aspect-cover into box with pan on excess."""
    tw, th = target_width, target_height
    width_ratio = tw / img.width
    height_ratio = th / img.height
    ratio = max(width_ratio, height_ratio)
    new_width = max(1, int(img.width * ratio))
    new_height = max(1, int(img.height * ratio))
    img = img.resize((new_width, new_height), Image.Resampling.BILINEAR)
    excess_w = new_width - tw
    excess_h = new_height - th
    left = int(round((1.0 + pan_x) * excess_w / 2.0)) if excess_w > 0 else 0
    top = int(round((1.0 + pan_y) * excess_h / 2.0)) if excess_h > 0 else 0
    left = max(0, min(excess_w, left))
    top = max(0, min(excess_h, top))
    return img.crop((left, top, left + tw, top + th))


def _process_image_impl(
    image_path: Path,
    target_width: int,
    target_height: int,
    required_orientation: str,
    pan_x: float = 0.0,
    pan_y: float = 0.0,
    *,
    flip_h: bool = False,
    grayscale: bool = False,
):
    """pan_x / pan_y in [-1, 1]: shift the crop window (0 = centered)."""
    pan_x = max(-1.0, min(1.0, float(pan_x)))
    pan_y = max(-1.0, min(1.0, float(pan_y)))
    try:
        with Image.open(image_path) as img:
            if img.format == "JPEG":
                img.draft("RGB", (target_width, target_height))

            img = fix_orientation(img)

            is_horizontal = img.width > img.height
            if required_orientation == "horizontal" and not is_horizontal:
                return None
            if required_orientation == "vertical" and is_horizontal:
                return None

            img = flatten_alpha(img)
            tw, th = target_width, target_height
            img = _cover_resize_and_crop_panned(img, tw, th, pan_x, pan_y)

            if flip_h:
                img = ImageOps.mirror(img)
            if grayscale:
                img = ImageOps.grayscale(img).convert("RGB")
            return img

    except Exception as e:
        logger.debug("process_image failed %s: %s", image_path, e)
        return None


def _process_image_centered(
    image_path: Path, target_width: int, target_height: int, required_orientation: str
):
    return _process_image_impl(
        image_path,
        target_width,
        target_height,
        required_orientation,
        0.0,
        0.0,
        flip_h=False,
        grayscale=False,
    )


process_image = lru_cache(maxsize=384)(_process_image_centered)


def process_image_panned(
    image_path: Path,
    target_width: int,
    target_height: int,
    required_orientation: str,
    pan_x: float = 0.0,
    pan_y: float = 0.0,
    *,
    flip_h: bool = False,
    grayscale: bool = False,
):
    """Uncached crop with pan; use for manual export and interactive preview."""
    return _process_image_impl(
        image_path,
        target_width,
        target_height,
        required_orientation,
        pan_x,
        pan_y,
        flip_h=flip_h,
        grayscale=grayscale,
    )


_PREVIEW_CELL_LOCK = threading.Lock()
_PREVIEW_CELL_CACHE: OrderedDict[tuple, Image.Image] = OrderedDict()
PREVIEW_CELL_CACHE_MAX = 128


def clear_preview_cell_cache() -> None:
    with _PREVIEW_CELL_LOCK:
        _PREVIEW_CELL_CACHE.clear()


def process_image_preview_cell(
    path: Path,
    tw: int,
    th: int,
    orient: str,
    pan_x: float = 0.0,
    pan_y: float = 0.0,
    *,
    flip_h: bool = False,
    grayscale: bool = False,
):
    """Mtime-aware LRU for live manual preview (resize / reassign / pan / flip / B&W)."""
    path = path.resolve()
    try:
        st = path.stat()
        mt = int(getattr(st, "st_mtime_ns", int(st.st_mtime * 1e9)))
    except OSError:
        return _process_image_impl(
            path, tw, th, orient, pan_x, pan_y, flip_h=flip_h, grayscale=grayscale
        )

    prx = round(float(pan_x), 3)
    pry = round(float(pan_y), 3)
    key = (str(path), tw, th, orient, mt, prx, pry, bool(flip_h), bool(grayscale))
    with _PREVIEW_CELL_LOCK:
        hit = _PREVIEW_CELL_CACHE.get(key)
        if hit is not None:
            _PREVIEW_CELL_CACHE.move_to_end(key)
            return hit.copy()

    out = _process_image_impl(path, tw, th, orient, pan_x, pan_y, flip_h=flip_h, grayscale=grayscale)
    if out is None:
        return None
    with _PREVIEW_CELL_LOCK:
        while len(_PREVIEW_CELL_CACHE) >= PREVIEW_CELL_CACHE_MAX:
            _PREVIEW_CELL_CACHE.popitem(last=False)
        _PREVIEW_CELL_CACHE[key] = out.copy()
    return out


def create_collage(
    processed_images,
    canvas_color: Tuple[int, int, int],
    positions: list[tuple[int, int]],
    num_images: int,
    *,
    canvas_size: tuple[int, int] | None = None,
) -> Image.Image:
    w = CANVAS_WIDTH if canvas_size is None else canvas_size[0]
    h = CANVAS_HEIGHT if canvas_size is None else canvas_size[1]
    canvas = Image.new("RGB", (w, h), color=canvas_color)
    for idx, img in enumerate(processed_images):
        canvas.paste(img, positions[idx])
    return canvas


def generate_output_filename(output_dir: Path, prefix: str) -> Path:
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    return output_dir / f"{prefix}_{timestamp}_{random.randint(100, 999)}.jpg"


def get_layout_geometry(layout_name: str, borderless: bool, bleed: bool = False) -> dict[str, Any]:
    config = LAYOUT_CONFIG[layout_name]
    rows, cols = config["rows"], config["cols"]
    is_framed = config["framed"]
    if borderless:
        spacing = 0
        margin = 0
    else:
        if is_framed:
            spacing = 150
            margin = 150
        else:
            spacing = 75
            margin = 0

    total_spacing_h = (spacing * (rows - 1)) + (margin * 2)
    target_h = (CANVAS_HEIGHT - total_spacing_h) // rows
    total_spacing_w = (spacing * (cols - 1)) + (margin * 2)
    target_w_normal = (CANVAS_WIDTH - total_spacing_w) // cols

    use_bleed = bleed and not borderless and rows >= 1 and cols >= 1
    target_w_bleed_row = CANVAS_WIDTH // cols if use_bleed else target_w_normal

    positions: list[tuple[int, int]] = []
    cell_sizes: list[tuple[int, int]] = []

    for r in range(rows):
        if use_bleed and r == 0:
            tw = target_w_bleed_row
            for c in range(cols):
                positions.append((c * tw, margin + r * (target_h + spacing)))
                cell_sizes.append((tw, target_h))
        else:
            for c in range(cols):
                x = margin + c * (target_w_normal + spacing)
                y = margin + r * (target_h + spacing)
                positions.append((x, y))
                cell_sizes.append((target_w_normal, target_h))

    return {
        "num_images": config["num_images"],
        "orientation": config["orientation"],
        "target_w": target_w_normal,
        "target_h": target_h,
        "positions": positions,
        "cell_sizes": cell_sizes,
    }


def build_collage_image(
    ordered_paths: Sequence[str | Path],
    layout_name: str,
    borderless: bool,
    color: str | Tuple[int, int, int],
    *,
    bleed: bool = False,
    slot_pans: Sequence[tuple[float, float]] | None = None,
    slot_flip_h: Sequence[bool] | None = None,
    slot_grayscale: Sequence[bool] | None = None,
) -> Image.Image:
    geom = get_layout_geometry(layout_name, borderless, bleed)
    n = geom["num_images"]
    if len(ordered_paths) != n:
        raise ValueError(f"Need exactly {n} images for layout {layout_name!r}, got {len(ordered_paths)}.")
    if slot_pans is not None and len(slot_pans) != n:
        raise ValueError(f"slot_pans length must be {n}, got {len(slot_pans)}.")
    if slot_flip_h is not None and len(slot_flip_h) != n:
        raise ValueError(f"slot_flip_h length must be {n}, got {len(slot_flip_h)}.")
    if slot_grayscale is not None and len(slot_grayscale) != n:
        raise ValueError(f"slot_grayscale length must be {n}, got {len(slot_grayscale)}.")

    canvas_color = parse_color(color) if isinstance(color, str) else color
    orientation = geom["orientation"]
    cell_sizes = geom["cell_sizes"]
    processed = []
    for i, p in enumerate(ordered_paths):
        tw, th = cell_sizes[i]
        path = Path(p)
        if slot_pans is None:
            if layout_name == "grid-1x2-v":
                px = -1.0 if i == 0 else 1.0
                py = 0.0
            else:
                px, py = 0.0, 0.0
        else:
            px, py = slot_pans[i]
        fh = False if slot_flip_h is None else bool(slot_flip_h[i])
        gry = False if slot_grayscale is None else bool(slot_grayscale[i])
        img = process_image_panned(path, tw, th, orientation, px, py, flip_h=fh, grayscale=gry)
        if img is None:
            raise ValueError(f"Image unusable for this layout (orientation or read error): {path}")
        processed.append(img)
    return create_collage(processed, canvas_color, geom["positions"], n)


def run_collage_from_paths(
    ordered_paths: Sequence[str | Path],
    layout_name: str,
    borderless: bool,
    color: str | Tuple[int, int, int],
    output_path: Path | str,
    *,
    bleed: bool = False,
    slot_pans: Sequence[tuple[float, float]] | None = None,
    slot_flip_h: Sequence[bool] | None = None,
    slot_grayscale: Sequence[bool] | None = None,
) -> None:
    image = build_collage_image(
        ordered_paths,
        layout_name,
        borderless,
        color,
        bleed=bleed,
        slot_pans=slot_pans,
        slot_flip_h=slot_flip_h,
        slot_grayscale=slot_grayscale,
    )
    save_optimized(image, output_path)


def _imap_worker_job(job_tuple: tuple[Any, ...]) -> bool:
    """Pool imap helper (must be top-level for spawn)."""
    return worker_job(*job_tuple)


def worker_job(
    image_paths,
    job_index,
    prefix,
    output_dir,
    target_w,
    target_h,
    orientation,
    color,
    positions,
    num_images,
    cell_sizes=None,
):
    try:
        processed_images = []
        for i, p in enumerate(image_paths):
            tw = cell_sizes[i][0] if cell_sizes else target_w
            th = cell_sizes[i][1] if cell_sizes else target_h
            img = process_image(p, tw, th, orientation)
            if img is None:
                return False
            processed_images.append(img)
        if len(processed_images) == num_images:
            collage = create_collage(processed_images, color, positions, num_images)
            fname = generate_output_filename(output_dir, f"{prefix}_{job_index + 1:03d}")
            save_optimized(collage, fname)
            return True
        return False
    except Exception as e:
        logger.warning("[Worker] Job %s failed: %s", job_index + 1, e)
        return False


def prepare_layout_jobs(
    folder: str | Path,
    output_base: str | Path,
    layout_name: str,
    count: int,
    batch: bool,
    is_random: bool,
    borderless: bool,
    color: str | Tuple[int, int, int],
    bleed: bool = False,
) -> list[tuple[Any, ...]]:
    """Build worker_job argument tuples; empty list if nothing to run."""
    canvas_color = parse_color(color) if isinstance(color, str) else color
    geom = get_layout_geometry(layout_name, borderless, bleed)
    num_images = geom["num_images"]
    orientation_needed = geom["orientation"]
    target_w, target_h = geom["target_w"], geom["target_h"]
    positions = geom["positions"]
    cell_sizes = geom["cell_sizes"]

    valid_paths = get_valid_paths(folder, orientation_needed)
    if len(valid_paths) < num_images:
        return []
    combinations = generate_combinations(valid_paths, num_images, batch, is_random, count)
    if not combinations:
        return []
    output_dir = Path(output_base)
    output_dir.mkdir(parents=True, exist_ok=True)
    return [
        (
            combo,
            i,
            layout_name,
            output_dir,
            target_w,
            target_h,
            orientation_needed,
            canvas_color,
            positions,
            num_images,
            cell_sizes,
        )
        for i, combo in enumerate(combinations)
    ]


def run_jobs_pool(
    jobs: list[tuple[Any, ...]],
    *,
    progress_callback: Callable[[int, int], None] | None = None,
) -> None:
    if not jobs:
        return
    n_proc = min(8, multiprocessing.cpu_count(), len(jobs))
    with multiprocessing.Pool(processes=n_proc) as pool:
        for i, _ok in enumerate(pool.imap_unordered(_imap_worker_job, jobs, chunksize=1), start=1):
            if progress_callback is not None:
                progress_callback(i, len(jobs))


COMBO_JOB_SPECS: tuple[tuple[str, int, bool, bool, bool], ...] = (
    ("grid-2x4", 10, False, True, True),
    ("stack-3", 10, False, True, True),
    ("grid-2x2-v", 10, False, True, False),
    ("grid-2x2-v", 10, False, True, True),
)


def generate_combinations(all_paths, num_images, batch, random_mode, count):
    combinations = []
    total_files = len(all_paths)

    if total_files < num_images:
        return []

    if batch:
        pool = list(all_paths)
        if random_mode:
            random.shuffle(pool)

        num_grids = total_files // num_images
        for i in range(num_grids):
            combinations.append(pool[i * num_images : (i + 1) * num_images])

    elif random_mode:
        pool = list(all_paths)
        random.shuffle(pool)

        for _ in range(count):
            if len(pool) < num_images:
                pool = list(all_paths)
                random.shuffle(pool)

            combo = []
            for _ in range(num_images):
                combo.append(pool.pop())
            combinations.append(combo)

    else:
        combinations.append(all_paths[:num_images])

    return combinations


def list_layout_combinations(
    folder: str | Path,
    layout_name: str,
    count: int,
    batch: bool,
    is_random: bool,
    borderless: bool,
) -> list[list[Path]]:
    """Same path groupings Run would use (no disk output)."""
    geom = get_layout_geometry(layout_name, borderless)
    num_images = geom["num_images"]
    orientation_needed = geom["orientation"]
    valid_paths = get_valid_paths(folder, orientation_needed)
    if len(valid_paths) < num_images:
        return []
    return generate_combinations(valid_paths, num_images, batch, is_random, count)


def list_combo_preview_sequences(folder: str | Path) -> list[tuple[list[Path], str, bool]]:
    """(paths, layout_name, borderless) per combo output, same order as Run."""
    folder_p = Path(folder).resolve()
    out: list[tuple[list[Path], str, bool]] = []
    for layout_name, cnt, batch, rnd, bord in COMBO_JOB_SPECS:
        combos = list_layout_combinations(folder_p, layout_name, cnt, batch, rnd, bord)
        for c in combos:
            out.append((list(c), layout_name, bord))
    return out


def run_layout_job(
    folder,
    output_base,
    layout_name,
    count,
    batch,
    is_random,
    borderless,
    color,
    *,
    bleed: bool = False,
    progress_callback: Callable[[int, int], None] | None = None,
):
    """Execute one layout configuration (folder scan + multiprocessing pool)."""
    logger.info(
        "\n--- Running Task: %s | Mode: %s | Style: %s | Bleed: %s | Color: %s ---",
        layout_name.upper(),
        "Batch" if batch else f"Count {count}",
        "Borderless" if borderless else "Framed/Sep",
        "Yes" if bleed else "No",
        color,
    )

    jobs = prepare_layout_jobs(
        folder, output_base, layout_name, count, batch, is_random, borderless, color, bleed=bleed
    )
    if not jobs:
        geom = get_layout_geometry(layout_name, borderless, bleed)
        num_images = geom["num_images"]
        orientation_needed = geom["orientation"]
        valid_paths = get_valid_paths(folder, orientation_needed)
        if len(valid_paths) < num_images:
            logger.info(
                "[SKIP] Not enough %s images found (%s/%s).",
                orientation_needed,
                len(valid_paths),
                num_images,
            )
        else:
            logger.info("[SKIP] Could not generate valid combinations.")
        return

    logger.info("Processing %s collages...", len(jobs))
    run_jobs_pool(jobs, progress_callback=progress_callback)


def run_combo_job(
    folder: str | Path,
    output_base: str | Path,
    color: str | Tuple[int, int, int],
    *,
    bleed: bool = False,
    progress_callback: Callable[[int, int], None] | None = None,
) -> None:
    folder_s = str(Path(folder).resolve())
    out_s = str(output_base)
    jobs: list[tuple[Any, ...]] = []
    for layout_name, cnt, batch, rnd, bord in COMBO_JOB_SPECS:
        jobs.extend(
            prepare_layout_jobs(folder_s, out_s, layout_name, cnt, batch, rnd, bord, color, bleed=bleed)
        )
    if not jobs:
        logger.info("[SKIP] Combo: no collages to generate (check folders and orientations).")
        return
    logger.info("Processing %s combo collages...", len(jobs))
    run_jobs_pool(jobs, progress_callback=progress_callback)
