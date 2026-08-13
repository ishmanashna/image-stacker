"""Run: python -m app.perf  →  automated harness, report under ./perf_reports/"""

from app.perf.harness import main

if __name__ == "__main__":
    raise SystemExit(main())
