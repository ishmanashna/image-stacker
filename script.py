#!/usr/bin/env python3
"""CLI shim — same usage as before; implementation lives in ``app``."""

from app.cli import main

if __name__ == "__main__":
    raise SystemExit(main())
