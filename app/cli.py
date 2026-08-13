"""Command-line entry for Image Stacker (layout collages only)."""

from __future__ import annotations

import argparse
import logging
import sys
from pathlib import Path

from app.engine.layout_engine import LAYOUT_CONFIG, run_combo_job, run_layout_job
from app.io import parse_color

logging.basicConfig(level=logging.INFO, format="%(message)s")
logger = logging.getLogger(__name__)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Image Stacker — Instagram layout collages")
    parser.add_argument("folder", nargs="?", default=".", help="Folder with images")
    parser.add_argument("--layout", choices=list(LAYOUT_CONFIG.keys()), default="stack-3")
    parser.add_argument(
        "--combo",
        action="store_true",
        help='Run the "Standard Set": 10x 2x4(NB), 10x Stack3(NB), 10x 2x2(Framed), 10x 2x2(NB)',
    )
    parser.add_argument("--batch", action="store_true", help="Process ALL available images.")
    parser.add_argument("--random", action="store_true", help="Shuffle images.")
    parser.add_argument("--count", type=int, default=1, help="Number of posts (if not batch).")
    parser.add_argument("--output", default="output", help="Output folder")
    parser.add_argument("--borderless", action="store_true", help="Remove all borders/frames")
    parser.add_argument("--bleed", action="store_true", help="Top row edge-to-edge (editorial style)")
    parser.add_argument("--color", default="white", help="Color name (e.g., beige) or hex (e.g., #FFFFFF)")

    args = parser.parse_args(argv)
    folder = Path(args.folder).resolve()
    if not folder.is_dir():
        logger.error("Folder does not exist: %s", folder)
        return 1
    if args.count < 1:
        args.count = 1

    try:
        if args.combo:
            run_combo_job(folder, args.output, args.color, bleed=args.bleed)
        else:
            run_layout_job(
                str(folder),
                args.output,
                args.layout,
                args.count,
                args.batch,
                args.random,
                args.borderless,
                args.color,
                bleed=args.bleed,
            )
        logger.info("\nAll tasks finished.")
    except Exception as e:
        logger.exception("[FATAL] %s", e)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
