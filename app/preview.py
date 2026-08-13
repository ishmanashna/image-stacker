"""Low-resolution collage preview using the same layout math as full export."""

from __future__ import annotations

from pathlib import Path
from typing import Sequence

from PIL import Image

from app.engine.layout_engine import (
    CANVAS_HEIGHT,
    CANVAS_WIDTH,
    create_collage,
    get_layout_geometry,
    parse_color,
    process_image,
    process_image_panned,
)


def render_collage_preview(
    ordered_paths: Sequence[str | Path],
    layout_name: str,
    borderless: bool,
    color: str | tuple[int, int, int],
    *,
    bleed: bool = False,
    max_long_edge: int = 900,
) -> Image.Image:
    """Render a downscaled collage; geometry matches full export (scaled uniformly)."""
    scale = max_long_edge / max(CANVAS_WIDTH, CANVAS_HEIGHT)
    geom = get_layout_geometry(layout_name, borderless, bleed)
    n = geom["num_images"]
    if len(ordered_paths) != n:
        raise ValueError(f"Need exactly {n} images for layout {layout_name!r}, got {len(ordered_paths)}.")

    cw = max(1, int(CANVAS_WIDTH * scale))
    ch = max(1, int(CANVAS_HEIGHT * scale))
    positions = [(int(x * scale), int(y * scale)) for x, y in geom["positions"]]
    cell_sizes = geom["cell_sizes"]

    canvas_color = parse_color(color) if isinstance(color, str) else color
    orientation = geom["orientation"]
    processed = []
    for i, p in enumerate(ordered_paths):
        path = Path(p)
        tw = max(1, int(cell_sizes[i][0] * scale))
        th = max(1, int(cell_sizes[i][1] * scale))
        if layout_name == "grid-1x2-v":
            pan_x = -1.0 if i == 0 else 1.0
            img = process_image_panned(path, tw, th, orientation, pan_x, 0.0)
        else:
            img = process_image(path, tw, th, orientation)
        if img is None:
            raise ValueError(f"Image unusable for this layout: {path}")
        processed.append(img)
    return create_collage(processed, canvas_color, positions, n, canvas_size=(cw, ch))
