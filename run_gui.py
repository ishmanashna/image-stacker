"""Launch the desktop UI (development). Frozen builds should use ``app.gui.main`` as the PyInstaller entry."""

import multiprocessing

if __name__ == "__main__":
    multiprocessing.freeze_support()
    from app.gui.main import main

    main()
