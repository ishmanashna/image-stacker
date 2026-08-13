"""Main Tkinter window Ã¢â‚¬â€ Design variant 1: library column + vertical stage + slot fills."""

from __future__ import annotations

import json
import logging
import math
import random
from dataclasses import dataclass
import os
import subprocess
import sys
import time
import threading
import tkinter as tk
from pathlib import Path
from typing import Any
from tkinter import filedialog, messagebox, simpledialog, ttk
from tkinter.scrolledtext import ScrolledText

from PIL import Image, ImageDraw, ImageOps, ImageTk

from app.perf.tracker import record_since, span

from app.engine.layout_engine import (
    CANVAS_HEIGHT,
    CANVAS_WIDTH,
    LAYOUT_CONFIG,
    clear_preview_cell_cache,
    generate_output_filename,
    get_layout_geometry,
    get_valid_paths,
    list_combo_preview_sequences,
    list_layout_combinations,
    parse_color,
    process_image_preview_cell,
    run_collage_from_paths,
    run_combo_job,
    run_layout_job,
)
from app.preview import render_collage_preview

logger = logging.getLogger(__name__)


@dataclass
class ManualSlotFill:
    path: Path
    pan_x: float = 0.0
    pan_y: float = 0.0
    flip_h: bool = False
    grayscale: bool = False


THUMB_GRID_COLS = 3
# Thumbnail Ã¢â‚¬Å“tileÃ¢â‚¬Â is landscape (w:h); images use contain + letterbox, not square crop.
THUMB_TILE_AR_W = 3
THUMB_TILE_AR_H = 2
THUMB_TILE_BG = (0x2B, 0x2B, 0x2B)

LAYOUT_GRID_COLS = 3
LAYOUT_GRID_ORDER = list(LAYOUT_CONFIG.keys())
THUMB_DISPLAY_CAP = 200
THUMB_BATCH = 22
THUMB_PUMP_INTERVAL_MS = 20
PREVIEW_DEBOUNCE_MS = 320
FOLDER_PREVIEW_DEBOUNCE_MS = 550
MANUAL_STAGE_DEBOUNCE_MS = 48
# Strip GUI preview: compose wide mural at most this width, then scale to stage (export stays full-res).
RUN_ESTIMATE_DEBOUNCE_MS = 72
SETTINGS_NAME = "settings.json"
MANUAL_UNDO_MAX = 50

COLOR_PRESETS: list[tuple[str, str]] = [
    ("White", "white"),
    ("Black", "black"),
    ("Beige", "beige"),
    ("Ivory", "ivory"),
    ("Gray", "gray"),
    ("Light gray", "lightgray"),
    ("Dark gray", "darkgray"),
    ("Wheat", "wheat"),
    ("Tan", "tan"),
    ("Navy", "navy"),
    ("Maroon", "maroon"),
]

LAYOUT_CARD_TEXT: dict[str, tuple[str, str]] = {
    "stack-2": ("Stack 2", "2 photos Ã‚Â· H"),
    "stack-3": ("Stack 3", "3 photos Ã‚Â· H"),
    "grid-1x2-v": ("Split 1Ãƒâ€”2 V", "2 photos Ã‚Â· V Ã‚Â· opposite halves"),
    "grid-1x3-m": ("Row 1x3", "3 photos Ã‚Â· H or V"),
    "grid-2x4": ("Grid 2Ãƒâ€”4", "8 photos Ã‚Â· H"),
    "grid-3x3": ("Grid 3Ãƒâ€”3", "9 photos Ã‚Â· H"),
    "grid-2x2-v": ("Grid 2Ãƒâ€”2 V", "4 photos Ã‚Â· V Ã‚Â· frame"),
}


def _run_need_photos_phrase(n: int, orient: str, got: int) -> str:
    """User-facing text for 'not enough images' run estimates."""
    if orient == "mixed":
        return f"need at least {n} photos (H or V allowed); have {got}"
    return f"need at least {n} {orient} photos (have {got})"


def _app_data_dir() -> Path:
    base = os.environ.get("LOCALAPPDATA") or str(Path.home())
    d = Path(base) / "ImageStacker"
    d.mkdir(parents=True, exist_ok=True)
    return d


def _settings_file() -> Path:
    return _app_data_dir() / SETTINGS_NAME


class MainWindow:
    def __init__(self, root: tk.Tk) -> None:
        _t_init = time.perf_counter()
        self.root = root
        root.title("Image Stacker")
        root.minsize(1100, 720)

        self._busy = False
        self._slot_assignments: list[ManualSlotFill | None] = []
        self._stage_down_slot: int | None = None
        self._stage_press_xy: tuple[int, int] = (0, 0)
        self._stage_last_motion: tuple[int, int] | None = None
        self._stage_pan_moved = False
        self._pan_drag_slot: int | None = None
        self._pan_anchor: tuple[float, float] | None = None
        self._pan_live: tuple[float, float] | None = None
        self._swap_pickup_slot: int | None = None
        self._swap_hover_slot: int | None = None
        self._thumb_refs: list[ImageTk.PhotoImage] = []
        self._thumb_queue: list[Path] = []
        self._thumb_after_id: str | None = None
        self._preview_after_id: str | None = None
        self._folder_preview_after_id: str | None = None
        self._manual_stage_after_id: str | None = None
        self._run_estimate_after_id: str | None = None
        self._stage_image_id: int | None = None
        self._stage_photo: ImageTk.PhotoImage | None = None
        self._layout_card_buttons: dict[str, tk.Widget] = {}
        self._drag_thumb_path: Path | None = None
        self._stage_configure_after: str | None = None
        self._thumb_inner_window_id: int | None = None
        self._thumb_catalog_key: tuple[str, str] | None = None
        self._undo_manual_stack: list[list[ManualSlotFill | None]] = []
        self._redo_manual_stack: list[list[ManualSlotFill | None]] = []
        self._auto_preview_index: int = 0
        self.preview_nav_var = tk.StringVar(value="")
        self._auto_preview_entries_cache: list[tuple[list[Path], str, bool]] | None = None
        self._auto_preview_entries_key: tuple[Any, ...] | None = None
        self._last_run_output_dir: Path | None = None
        self._open_output_btn: ttk.Button | None = None

        self.folder_var = tk.StringVar(value=str(Path.cwd()))
        self.output_var = tk.StringVar(value="output")
        self.layout_var = tk.StringVar(value="stack-3")
        self.mode_var = tk.StringVar(value="single")
        self.count_var = tk.IntVar(value=1)
        self.borderless_var = tk.BooleanVar(value=False)
        self.bleed_var = tk.BooleanVar(value=False)
        self.color_var = tk.StringVar(value="white")
        self.run_info_var = tk.StringVar(value="")
        self.run_working_var = tk.StringVar(value="")

        self._reset_slots_for_layout()
        self._build_ui()
        self._wire_preview_traces()
        self._bind_global_mousewheel()
        self._bind_undo_keys()
        self._bind_preview_nav_keys()
        self._load_settings()
        self._last_committed_layout_key: str = self.layout_var.get()
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)
        record_since("gui.main_window.init", _t_init)

    def _reset_slots_for_layout(self) -> None:
        n = LAYOUT_CONFIG[self.layout_var.get()]["num_images"]
        self._slot_assignments = [None] * n
        self._clear_manual_undo()

    def _clear_manual_undo(self) -> None:
        self._undo_manual_stack.clear()
        self._redo_manual_stack.clear()

    def _clone_assignments(self) -> list[ManualSlotFill | None]:
        out: list[ManualSlotFill | None] = []
        for s in self._slot_assignments:
            if s is None:
                out.append(None)
            else:
                out.append(
                    ManualSlotFill(
                        Path(s.path), s.pan_x, s.pan_y, s.flip_h, s.grayscale
                    )
                )
        return out

    def _clone_from_snap(self, snap: list[ManualSlotFill | None]) -> list[ManualSlotFill | None]:
        out: list[ManualSlotFill | None] = []
        for s in snap:
            if s is None:
                out.append(None)
            else:
                out.append(
                    ManualSlotFill(
                        Path(s.path), s.pan_x, s.pan_y, s.flip_h, s.grayscale
                    )
                )
        return out

    def _manual_edit_checkpoint(self) -> None:
        if self.mode_var.get() != "manual":
            return
        self._undo_manual_stack.append(self._clone_assignments())
        if len(self._undo_manual_stack) > MANUAL_UNDO_MAX:
            self._undo_manual_stack.pop(0)
        self._redo_manual_stack.clear()

    def _manual_undo(self) -> None:
        if self.mode_var.get() != "manual" or not self._undo_manual_stack:
            return
        self._redo_manual_stack.append(self._clone_assignments())
        prev = self._undo_manual_stack.pop()
        self._slot_assignments = self._clone_from_snap(prev)
        self._schedule_manual_stage_paint(immediate=True)
        self._update_run_estimate()
        self.status_var.set("Undo.")

    def _manual_redo(self) -> None:
        if self.mode_var.get() != "manual" or not self._redo_manual_stack:
            return
        self._undo_manual_stack.append(self._clone_assignments())
        if len(self._undo_manual_stack) > MANUAL_UNDO_MAX:
            self._undo_manual_stack.pop(0)
        nxt = self._redo_manual_stack.pop()
        self._slot_assignments = self._clone_from_snap(nxt)
        self._schedule_manual_stage_paint(immediate=True)
        self._update_run_estimate()
        self.status_var.set("Redo.")

    def _bind_undo_keys(self) -> None:
        self.root.bind_all("<Control-z>", self._on_undo_key, add="+")
        self.root.bind_all("<Control-y>", self._on_redo_key, add="+")
        self.root.bind_all("<Control-Z>", self._on_redo_key, add="+")

    def _bind_preview_nav_keys(self) -> None:
        self.root.bind_all("<Left>", self._on_preview_left_key, add="+")
        self.root.bind_all("<Right>", self._on_preview_right_key, add="+")

    def _on_preview_left_key(self, _event: tk.Event) -> str | None:
        return self._on_preview_arrow_key(-1)

    def _on_preview_right_key(self, _event: tk.Event) -> str | None:
        return self._on_preview_arrow_key(1)

    def _on_preview_arrow_key(self, delta: int) -> str | None:
        w = self.root.focus_get()
        if w is not None and w.winfo_class() in ("Entry", "TEntry", "Spinbox", "TSpinbox", "Text"):
            return None
        if self.mode_var.get() == "manual":
            return None
        entries = self._auto_preview_entries()
        if not entries or len(entries) <= 1:
            return None
        self._nudge_auto_preview(delta)
        return "break"

    def _on_undo_key(self, _event: tk.Event) -> str | None:
        w = self.root.focus_get()
        if w is not None and w.winfo_class() in ("Entry", "TEntry", "Spinbox", "TSpinbox", "Text"):
            return None
        if self.mode_var.get() != "manual":
            return None
        self._manual_undo()
        return "break"

    def _on_redo_key(self, _event: tk.Event) -> str | None:
        w = self.root.focus_get()
        if w is not None and w.winfo_class() in ("Entry", "TEntry", "Spinbox", "TSpinbox", "Text"):
            return None
        if self.mode_var.get() != "manual":
            return None
        self._manual_redo()
        return "break"

    def _build_ui(self) -> None:
        main = ttk.Frame(self.root, padding=6)
        main.grid(row=0, column=0, sticky="nsew")
        self.root.rowconfigure(0, weight=1)
        self.root.columnconfigure(0, weight=1)
        main.rowconfigure(1, weight=1)
        main.columnconfigure(0, weight=3)
        main.columnconfigure(1, weight=6)
        main.columnconfigure(2, weight=1)

        top = ttk.Frame(main)
        top.grid(row=0, column=0, columnspan=3, sticky="ew", pady=(0, 6))
        ttk.Label(top, text="Input").pack(side=tk.LEFT, padx=(0, 4))
        ttk.Entry(top, textvariable=self.folder_var, width=28).pack(side=tk.LEFT, padx=(0, 4))
        ttk.Button(top, text="BrowseÃ¢â‚¬Â¦", command=self._browse_input).pack(side=tk.LEFT, padx=(0, 12))
        ttk.Label(top, text="Output").pack(side=tk.LEFT, padx=(0, 4))
        ttk.Entry(top, textvariable=self.output_var, width=20).pack(side=tk.LEFT, padx=(0, 4))
        ttk.Button(top, text="BrowseÃ¢â‚¬Â¦", command=self._browse_output).pack(side=tk.LEFT, padx=(0, 12))

        left_col = ttk.Frame(main)
        left_col.grid(row=1, column=0, sticky="nsew", padx=(0, 4))
        left_col.rowconfigure(1, weight=1)
        left_col.columnconfigure(0, weight=1)

        layout_shell = ttk.LabelFrame(left_col, text="Layout", padding=4)
        layout_shell.grid(row=0, column=0, sticky="ew", pady=(0, 6))
        self._layout_inner = ttk.Frame(layout_shell)
        self._layout_inner.pack(fill=tk.BOTH, expand=True)
        for c in range(LAYOUT_GRID_COLS):
            self._layout_inner.columnconfigure(c, weight=1, uniform="lay")
        n_layout_rows = (len(LAYOUT_GRID_ORDER) + LAYOUT_GRID_COLS - 1) // LAYOUT_GRID_COLS
        for r in range(n_layout_rows):
            self._layout_inner.rowconfigure(r, weight=1, uniform="layr")

        for idx, key in enumerate(LAYOUT_GRID_ORDER):
            title, sub = LAYOUT_CARD_TEXT.get(key, (key, ""))
            txt = f"{title}\n{sub}"
            btn = tk.Button(
                self._layout_inner,
                text=txt,
                justify=tk.CENTER,
                anchor="center",
                padx=4,
                pady=6,
                font=("Segoe UI", 8),
                wraplength=110,
                command=lambda k=key: self._select_layout(k),
            )
            btn.grid(row=idx // LAYOUT_GRID_COLS, column=idx % LAYOUT_GRID_COLS, sticky="nsew", padx=2, pady=2)
            self._layout_card_buttons[key] = btn
        self._highlight_layout_card()

        thumb_shell = ttk.LabelFrame(left_col, text="Photos (scroll)", padding=4)
        thumb_shell.grid(row=1, column=0, sticky="nsew")
        thumb_outer = ttk.Frame(thumb_shell)
        thumb_outer.pack(fill=tk.BOTH, expand=True)
        thumb_outer.rowconfigure(0, weight=1)
        thumb_outer.columnconfigure(0, weight=1)
        self.thumb_canvas = tk.Canvas(thumb_outer, highlightthickness=0, background="#2b2b2b")
        self.thumb_scroll = ttk.Scrollbar(thumb_outer, orient=tk.VERTICAL, command=self.thumb_canvas.yview)
        self.thumb_inner = ttk.Frame(self.thumb_canvas)
        self.thumb_inner.bind("<Configure>", lambda _e: self.thumb_canvas.configure(scrollregion=self.thumb_canvas.bbox("all")))
        self._thumb_inner_window_id = self.thumb_canvas.create_window((0, 0), window=self.thumb_inner, anchor="nw")
        self.thumb_canvas.configure(yscrollcommand=self.thumb_scroll.set)
        self.thumb_canvas.bind("<Configure>", self._on_thumb_canvas_configure)
        self.thumb_canvas.grid(row=0, column=0, sticky="nsew")
        self.thumb_scroll.grid(row=0, column=1, sticky="ns")
        for c in range(THUMB_GRID_COLS):
            self.thumb_inner.columnconfigure(c, weight=1, uniform="thc")

        ttk.Button(
            thumb_shell, text="Refresh photos", command=lambda: self._schedule_thumb_reload(force=True)
        ).pack(anchor="w", pady=(4, 0))

        stage_frame = ttk.LabelFrame(
            main,
            text="Preview (4:5) Ã¢â‚¬â€ non-manual: Ã¢â€ Â Ã¢â€ â€™ browse Ã‚Â· Use in manual. Manual: drop / pan / flip / swap / clear.",
            padding=4,
        )
        stage_frame.grid(row=1, column=1, sticky="nsew", padx=4)
        stage_frame.rowconfigure(1, weight=1)
        stage_frame.columnconfigure(0, weight=1)
        self._preview_bar = ttk.Frame(stage_frame)
        self._preview_bar.grid(row=0, column=0, sticky="ew", pady=(0, 4))
        self._preview_prev_btn = ttk.Button(self._preview_bar, text="Ã¢â€”â‚¬", width=3, command=lambda: self._nudge_auto_preview(-1))
        self._preview_prev_btn.pack(side=tk.LEFT, padx=(0, 4))
        ttk.Label(self._preview_bar, textvariable=self.preview_nav_var, width=14, anchor="center").pack(
            side=tk.LEFT, padx=4
        )
        self._preview_next_btn = ttk.Button(self._preview_bar, text="Ã¢â€“Â¶", width=3, command=lambda: self._nudge_auto_preview(1))
        self._preview_next_btn.pack(side=tk.LEFT, padx=(4, 12))
        self._use_manual_btn = ttk.Button(
            self._preview_bar, text="Use in manual", command=self._take_preview_to_manual
        )
        self._use_manual_btn.pack(side=tk.LEFT)
        self.stage = tk.Canvas(stage_frame, highlightthickness=0, bg="#141414")
        self.stage.grid(row=1, column=0, sticky="nsew")
        self._stage_hscroll = ttk.Scrollbar(stage_frame, orient=tk.HORIZONTAL, command=self.stage.xview)
        self._stage_hscroll.grid(row=2, column=0, sticky="ew")
        self._stage_hscroll.grid_remove()
        self.stage.bind("<Configure>", self._on_stage_configure)
        self.stage.bind("<Button-1>", self._on_stage_button1_press)
        self.stage.bind("<B1-Motion>", self._on_stage_b1_motion)
        self.stage.bind("<ButtonRelease-1>", self._on_stage_button1_release)
        self.stage.bind("<Double-Button-1>", self._on_stage_double_click)
        self.stage.bind("<Button-3>", self._on_stage_right_click)

        right = ttk.Frame(main)
        right.grid(row=1, column=2, sticky="nsew", padx=(4, 0))
        right.columnconfigure(0, weight=1)
        right.rowconfigure(0, weight=1)

        mode_style = ttk.LabelFrame(right, text="Mode & style", padding=4)
        mode_style.grid(row=0, column=0, sticky="nsew")
        r = 0
        ttk.Label(mode_style, text="Mode").grid(row=r, column=0, sticky="nw")
        mode_frame = ttk.Frame(mode_style)
        mode_frame.grid(row=r, column=1, sticky="w", pady=(0, 8))
        for i, (val, label) in enumerate(
            [
                ("single", "Single (count)"),
                ("batch", "Batch all"),
                ("random", "Random Ãƒâ€” count"),
                ("combo", "Combo pack"),                ("manual", "Manual"),
            ]
        ):
            ttk.Radiobutton(mode_frame, text=label, variable=self.mode_var, value=val, command=self._on_mode_change).grid(
                row=i, column=0, sticky="w"
            )
        r += 1
        ttk.Label(mode_style, text="Count").grid(row=r, column=0, sticky="w")
        self.count_spin = ttk.Spinbox(mode_style, from_=1, to=999, textvariable=self.count_var, width=8)
        self.count_spin.grid(row=r, column=1, sticky="w", pady=4)
        r += 1
        self._borderless_chk = ttk.Checkbutton(
            mode_style, text="Borderless", variable=self.borderless_var, command=self._on_borderless_changed
        )
        self._borderless_chk.grid(row=r, column=1, sticky="w", pady=4)
        r += 1
        self._bleed_chk = ttk.Checkbutton(
            mode_style, text="Bleed (top row edge-to-edge)", variable=self.bleed_var, command=self._on_bleed_changed
        )
        self._bleed_chk.grid(row=r, column=1, sticky="w", pady=4)
        r += 1
        ttk.Label(mode_style, text="Color").grid(row=r, column=0, sticky="w")
        color_wrap = ttk.Frame(mode_style)
        color_wrap.grid(row=r, column=1, sticky="w", pady=4)
        self._color_mbtn = tk.Menubutton(color_wrap, relief=tk.RAISED, width=16, anchor="w")
        self._color_menu = tk.Menu(self._color_mbtn, tearoff=0)
        for lbl, val in COLOR_PRESETS:
            self._color_menu.add_command(label=lbl, command=lambda v=val: self._set_color_preset(v))
        self._color_menu.add_separator()
        self._color_menu.add_command(label="CustomÃ¢â‚¬Â¦", command=self._color_custom_dialog)
        self._color_mbtn["menu"] = self._color_menu
        self._color_mbtn.pack(side=tk.LEFT)
        r += 1
        self.status_var = tk.StringVar(value="Ready.")
        ttk.Label(mode_style, textvariable=self.status_var, wraplength=180).grid(
            row=r, column=0, columnspan=2, sticky="w", pady=(12, 0)
        )
        self._sync_color_button_text()

        run_frame = ttk.LabelFrame(right, text="Run", padding=4)
        run_frame.grid(row=1, column=0, sticky="ew", pady=(10, 0))
        run_frame.columnconfigure(0, weight=1)
        ttk.Label(run_frame, textvariable=self.run_info_var, wraplength=180, justify=tk.LEFT).grid(row=0, column=0, sticky="w")
        self.run_progress = ttk.Progressbar(run_frame, mode="determinate", length=180, maximum=1, value=0)
        self.run_progress.grid(row=1, column=0, sticky="ew", pady=(8, 0))
        self.run_progress.grid_remove()
        ttk.Label(run_frame, textvariable=self.run_working_var, wraplength=180, justify=tk.LEFT).grid(row=2, column=0, sticky="w", pady=(2, 0))
        ttk.Button(run_frame, text="ShortcutsÃ¢â‚¬Â¦", command=self._show_shortcuts_dialog).grid(
            row=3, column=0, sticky="ew", pady=(8, 0)
        )
        ttk.Button(run_frame, text="Run", command=self._on_run).grid(row=4, column=0, sticky="ew", pady=(10, 0))
        self._open_output_btn = ttk.Button(
            run_frame,
            text="Open output folder",
            command=self._open_last_output_folder,
            state="disabled",
        )
        self._open_output_btn.grid(row=5, column=0, sticky="ew", pady=(6, 0))
        self._update_run_estimate(immediate=True)

    def _on_thumb_canvas_configure(self, event: tk.Event) -> None:
        if self._thumb_inner_window_id is None:
            return
        w = max(event.width, 1)
        self.thumb_canvas.itemconfigure(self._thumb_inner_window_id, width=w)

    def _bind_global_mousewheel(self) -> None:
        self.root.bind_all("<MouseWheel>", self._on_global_mousewheel, add="+")
        self.root.bind_all("<Button-4>", self._on_global_mousewheel, add="+")
        self.root.bind_all("<Button-5>", self._on_global_mousewheel, add="+")

    def _on_global_mousewheel(self, event: tk.Event) -> None:
        w = self.root.winfo_containing(event.x_root, event.y_root)
        if w is None:
            return
        delta = getattr(event, "delta", 0) or 0
        if getattr(event, "num", None) == 4:
            delta = 120
        elif getattr(event, "num", None) == 5:
            delta = -120
        if delta == 0:
            return
        steps = int(-delta / 120)
        if steps == 0:
            steps = -1 if delta > 0 else 1
        if self._widget_is_descendant(w, self.thumb_canvas):
            self.thumb_canvas.yview_scroll(steps, "units")
            return

    @staticmethod
    def _widget_is_descendant(w: tk.Misc | None, ancestor: tk.Misc) -> bool:
        cur: tk.Misc | None = w
        while cur is not None:
            if cur == ancestor:
                return True
            cur = getattr(cur, "master", None)
        return False

    def _select_layout(self, key: str) -> None:
        with span("gui.interaction.layout_card_pick"):
            if self.mode_var.get() == "combo":
                return
        self.layout_var.set(key)
        self._on_layout_changed()

    def _highlight_layout_card(self) -> None:
        cur = self.layout_var.get()
        for k, btn in self._layout_card_buttons.items():
            if k == cur:
                btn.config(relief=tk.SUNKEN, bg="#d0e8ff")
            else:
                btn.config(relief=tk.RAISED, bg="SystemButtonFace")

    def _wire_preview_traces(self) -> None:
        self.mode_var.trace_add(
            "write",
            lambda *_: (self._reset_auto_preview_index(), self._schedule_preview()),
        )
        self.count_var.trace_add(
            "write",
            lambda *_: (
                self._reset_auto_preview_index(),
                self._schedule_preview(),
                self._update_run_estimate(),
            ),
        )
        self.folder_var.trace_add(
            "write",
            lambda *_: (
                self._reset_auto_preview_index(),
                self._schedule_folder_preview(),
            ),
        )

    def _reset_auto_preview_index(self) -> None:
        self._auto_preview_index = 0
        self._auto_preview_entries_cache = None
        self._auto_preview_entries_key = None

    def _on_borderless_changed(self) -> None:
        self._reset_auto_preview_index()
        self._schedule_preview()

    def _on_bleed_changed(self) -> None:
        self._reset_auto_preview_index()
        self._schedule_preview()

    def _set_color_preset(self, value: str) -> None:
        self.color_var.set(value)
        self._sync_color_button_text()
        self._schedule_preview()


    def _color_custom_dialog(self) -> None:
        cur = self.color_var.get()
        s = simpledialog.askstring("Custom color", "Name or #RRGGBB:", initialvalue=cur, parent=self.root)
        if s and s.strip():
            self.color_var.set(s.strip())
            self._sync_color_button_text()
            self._schedule_preview()


    def _sync_color_button_text(self) -> None:
        t = self.color_var.get() or "white"
        self._color_mbtn.config(text=t[:18] + ("Ã¢â‚¬Â¦" if len(t) > 18 else ""))

    def _on_layout_changed(self) -> None:
        with span("gui.interaction.layout_change"):
            self._reset_auto_preview_index()
            prev_key = self._last_committed_layout_key
            new_key = self.layout_var.get()
            self._reset_slots_for_layout()
            self._highlight_layout_card()
            if self.mode_var.get() == "manual":
                prev_o = (
                    LAYOUT_CONFIG[prev_key]["orientation"] if prev_key in LAYOUT_CONFIG else None
                )
                new_o = LAYOUT_CONFIG[new_key]["orientation"]
                if prev_o != new_o:
                    self._schedule_thumb_reload(force=True)
            self._last_committed_layout_key = new_key
        self._schedule_preview()
        self._redraw_stage_soon()
        self._update_run_estimate()

    def _browse_input(self) -> None:
        p = filedialog.askdirectory(initialdir=self.folder_var.get() or ".")
        if p:
            self.folder_var.set(p)
            self._schedule_thumb_reload(force=True)
            self._schedule_preview()

    def _browse_output(self) -> None:
        p = filedialog.askdirectory(initialdir=self.output_var.get() or ".")
        if p:
            self.output_var.set(p)

    def _cancel_manual_stage_paint(self) -> None:
        if self._manual_stage_after_id:
            try:
                self.root.after_cancel(self._manual_stage_after_id)
            except Exception:
                pass
            self._manual_stage_after_id = None

    def _schedule_manual_stage_paint(self, *, immediate: bool = False) -> None:
        if self.mode_var.get() != "manual":
            return
        self._cancel_manual_stage_paint()
        if immediate:
            self.root.after_idle(self._paint_stage)
            return
        self._manual_stage_after_id = self.root.after(
            MANUAL_STAGE_DEBOUNCE_MS, self._fire_manual_stage_paint
        )

    def _fire_manual_stage_paint(self) -> None:
        self._manual_stage_after_id = None
        if self.mode_var.get() == "manual":
            self._paint_stage()

    def _on_mode_change(self) -> None:
        with span("gui.interaction.mode_change"):
            mode = self.mode_var.get()
            if mode != "manual":
                self._cancel_manual_stage_paint()
                self._swap_pickup_slot = None
                self._swap_hover_slot = None
                self._pan_drag_slot = None
                self._pan_anchor = None
                self._pan_live = None
            for btn in self._layout_card_buttons.values():
                btn.config(state=(tk.DISABLED if mode == "combo" else tk.NORMAL))
            if mode == "manual":
                self.count_spin.state(["disabled"])
                self._stage_last_configure_size = None
                self._schedule_thumb_reload(force=False)
                self.status_var.set(
                    "Manual: drag/click thumbs to fill; drag photo to pan; double-click flip; Shift+double-click B&W; "
                    "Ctrl+drag swap; right-click clear; Ctrl+Z / Ctrl+Y undo/redo."
                )
                self._reset_slots_for_layout()
            else:
                if mode in ("single", "random"):
                    self.count_spin.state(["!disabled"])
                else:
                    self.count_spin.state(["disabled"])
                self.status_var.set("Ready.")
        self._reset_slots_for_layout()
        self._schedule_preview()
        self._redraw_stage_soon()
        self._update_run_estimate()
        self._sync_preview_nav_ui()


    def _thumb_press(self, path: Path) -> None:
        self._drag_thumb_path = path

    def _thumb_release(self, event: tk.Event, path: Path) -> None:
        with span("gui.interaction.thumb_release"):
            if self.mode_var.get() != "manual":
                self._drag_thumb_path = None
                return
            wx = self.stage.winfo_rootx()
            wy = self.stage.winfo_rooty()
            ex, ey = event.x_root, event.y_root
            if wx <= ex <= wx + self.stage.winfo_width() and wy <= ey <= wy + self.stage.winfo_height():
                slot = self._hit_test_slot(ex - wx, ey - wy)
                if slot is not None:
                    self._assign_to_slot(slot, path)
                    self._drag_thumb_path = None
                    return
            self._assign_thumb_click(path)
            self._drag_thumb_path = None


    def _assign_thumb_click(self, path: Path) -> None:
        for i, s in enumerate(self._slot_assignments):
            if s is None:
                self._assign_to_slot(i, path)
                return
        self.status_var.set("All slots are full — right-click a slot on the preview to clear one.")


    def _assign_to_slot(self, slot: int, path: Path) -> None:
        with span("gui.interaction.assign_to_slot"):
            n_manual = self._required_count()
            if not (0 <= slot < n_manual):
                return
            self._manual_edit_checkpoint()
            pan_x = 0.0
            pan_y = 0.0
            if self.layout_var.get() == "grid-1x2-v" and n_manual == 2:
                pan_x = -1.0 if slot == 0 else 1.0
            self._slot_assignments[slot] = ManualSlotFill(path, pan_x=pan_x, pan_y=pan_y)
            self._schedule_manual_stage_paint()
            self._update_run_estimate()


    @staticmethod
    def _event_has_swap_modifier(event: tk.Event) -> bool:
        """Control (Windows/Linux); bit layout is platform-consistent in Tk for Control."""
        return (getattr(event, "state", 0) & 0x0004) != 0

    @staticmethod
    def _event_has_shift_modifier(event: tk.Event) -> bool:
        return (getattr(event, "state", 0) & 0x0001) != 0

    def _swap_slot_fills(self, a: int, b: int) -> None:
        n = len(self._slot_assignments)
        if not (0 <= a < n and 0 <= b < n) or a == b:
            return
        self._manual_edit_checkpoint()
        self._slot_assignments[a], self._slot_assignments[b] = (
            self._slot_assignments[b],
            self._slot_assignments[a],
        )


    def _stage_metrics(self) -> tuple[float, float, float] | None:
        """scale, offset_x, offset_y for mapping CANVAS coords to stage pixels."""
        sw = max(self.stage.winfo_width(), 50)
        sh = max(self.stage.winfo_height(), 50)
        scale = min(sw / CANVAS_WIDTH, sh / CANVAS_HEIGHT)
        cw = CANVAS_WIDTH * scale
        ch = CANVAS_HEIGHT * scale
        ox = (sw - cw) / 2
        oy = (sh - ch) / 2
        return scale, ox, oy

    def _slot_pixel_rects(
        self, scale: float, ox: float, oy: float, geom: dict
    ) -> list[tuple[int, int, int, int]]:
        """Floor/ceil edges so adjacent tiles overlap slightly Ã¢â‚¬â€ no hairline gaps in preview."""
        cell_sizes = geom.get("cell_sizes")
        rects: list[tuple[int, int, int, int]] = []
        for i, (cx, cy) in enumerate(geom["positions"]):
            tw, th = cell_sizes[i] if cell_sizes else (geom["target_w"], geom["target_h"])
            x0 = int(math.floor(ox + cx * scale))
            y0 = int(math.floor(oy + cy * scale))
            x1 = int(math.ceil(ox + (cx + tw) * scale))
            y1 = int(math.ceil(oy + (cy + th) * scale))
            rects.append((x0, y0, max(x0 + 1, x1), max(y0 + 1, y1)))
        return rects

    def _hit_test_slot(self, px: float, py: float) -> int | None:
        m = self._stage_metrics()
        if m is None:
            return None
        scale, ox, oy = m
        geom = get_layout_geometry(self.layout_var.get(), self.borderless_var.get(), self.bleed_var.get())
        rects = self._slot_pixel_rects(scale, ox, oy, geom)
        for i in range(len(rects) - 1, -1, -1):
            x0, y0, x1, y1 = rects[i]
            if x0 <= px < x1 and y0 <= py < y1:
                return i
        return None


    def _on_stage_double_click(self, event: tk.Event) -> None:
        if self.mode_var.get() != "manual":
            return
        self._stage_down_slot = None
        self._stage_last_motion = None
        self._stage_pan_moved = False
        self._pan_drag_slot = None
        self._pan_anchor = None
        self._pan_live = None
        self._swap_pickup_slot = None
        self._swap_hover_slot = None
        slot = self._hit_test_slot(event.x, event.y)
        if slot is None:
            return "break"
        fill = self._slot_assignments[slot] if slot < len(self._slot_assignments) else None
        if fill is None:
            return "break"
        self._manual_edit_checkpoint()
        if self._event_has_shift_modifier(event):
            fill.grayscale = not fill.grayscale
            self.status_var.set(
                f"Slot {slot + 1}: black & white {'on' if fill.grayscale else 'off'}."
            )
        else:
            fill.flip_h = not fill.flip_h
            self.status_var.set(
                f"Slot {slot + 1}: horizontal flip {'on' if fill.flip_h else 'off'}."
            )
        self._schedule_manual_stage_paint(immediate=True)
        return "break"

    def _on_stage_button1_press(self, event: tk.Event) -> None:
        if self.mode_var.get() != "manual":
            return
        self._swap_hover_slot = None
        slot = self._hit_test_slot(event.x, event.y)
        if self._event_has_swap_modifier(event) and slot is not None:
            fill = self._slot_assignments[slot] if slot < len(self._slot_assignments) else None
            if fill is not None:
                self._swap_pickup_slot = slot
                self._stage_down_slot = None
                self.status_var.set(
                    f"Slot {slot + 1} picked Ã¢â‚¬â€ release on another slot to swap (crops stay with each photo)."
                )
                self._schedule_manual_stage_paint(immediate=True)
                return
        self._swap_pickup_slot = None
        self._stage_down_slot = slot
        self._stage_press_xy = (event.x, event.y)
        self._stage_last_motion = (event.x, event.y)
        self._stage_pan_moved = False
        self._pan_drag_slot = None
        self._pan_anchor = None
        self._pan_live = None

    def _slot_pan_sensitivity(self, slot: int) -> tuple[float, float] | None:
        m = self._stage_metrics()
        if m is None:
            return None
        scale, ox, oy = m
        geom = get_layout_geometry(self.layout_var.get(), self.borderless_var.get(), self.bleed_var.get())
        rects = self._slot_pixel_rects(scale, ox, oy, geom)
        if slot >= len(rects):
            return None
        x0, y0, x1, y1 = rects[slot]
        tws = max(1, x1 - x0)
        ths = max(1, y1 - y0)
        return 2.0 / tws, 2.0 / ths


    def _on_stage_b1_motion(self, event: tk.Event) -> None:
        if self.mode_var.get() != "manual":
            return
        if self._swap_pickup_slot is not None:
            self._swap_hover_slot = self._hit_test_slot(event.x, event.y)
            self._schedule_manual_stage_paint(immediate=True)
            return
        slot = self._stage_down_slot
        if slot is None:
            return
        fill = self._slot_assignments[slot] if slot < len(self._slot_assignments) else None
        if fill is None:
            return
        tcx = event.x - self._stage_press_xy[0]
        tcy = event.y - self._stage_press_xy[1]
        if not self._stage_pan_moved:
            if tcx * tcx + tcy * tcy < 36:
                return
            self._stage_pan_moved = True
            self._pan_drag_slot = slot
            self._pan_anchor = (fill.pan_x, fill.pan_y)
        sens = self._slot_pan_sensitivity(slot)
        if sens is None or self._pan_anchor is None:
            return
        sens_x, sens_y = sens
        ax, ay = self._pan_anchor
        self._pan_live = (
            max(-1.0, min(1.0, ax - tcx * sens_x)),
            max(-1.0, min(1.0, ay - tcy * sens_y)),
        )
        self._schedule_manual_stage_paint(immediate=True)

    def _on_stage_button1_release(self, event: tk.Event) -> None:
        if self.mode_var.get() != "manual":
            self._stage_down_slot = None
            self._swap_pickup_slot = None
            self._swap_hover_slot = None
            return
        if self._swap_pickup_slot is not None:
            src = self._swap_pickup_slot
            tgt = self._hit_test_slot(event.x, event.y)
            self._swap_pickup_slot = None
            self._swap_hover_slot = None
            if tgt is not None and tgt != src:
                self._swap_slot_fills(src, tgt)
                self.status_var.set(f"Swapped slots {src + 1} and {tgt + 1}.")
            else:
                self.status_var.set("Swap cancelled.")
            self._schedule_manual_stage_paint(immediate=True)
            self._update_run_estimate()
            self._stage_down_slot = None
            self._stage_last_motion = None
            self._stage_pan_moved = False
            return
        if self._stage_pan_moved and self._pan_drag_slot is not None and self._pan_live is not None:
            fill = self._slot_assignments[self._pan_drag_slot]
            if fill is not None:
                self._manual_edit_checkpoint()
                fill.pan_x, fill.pan_y = self._pan_live
            self._schedule_manual_stage_paint(immediate=True)
        self._pan_drag_slot = None
        self._pan_anchor = None
        self._pan_live = None
        self._stage_down_slot = None
        self._stage_last_motion = None
        self._stage_pan_moved = False

    def _on_stage_right_click(self, event: tk.Event) -> None:
        if self.mode_var.get() != "manual":
            return
        slot = self._hit_test_slot(event.x, event.y)
        if slot is not None:
            self._manual_edit_checkpoint()
            self._slot_assignments[slot] = None
            self._schedule_manual_stage_paint()
            self._update_run_estimate()

    def _on_stage_configure(self, _event=None) -> None:
        sw = max(self.stage.winfo_width(), 50)
        sh = max(self.stage.winfo_height(), 50)
        if (sw, sh) == self._stage_last_configure_size:
            return
        self._stage_last_configure_size = (sw, sh)
        if self._stage_configure_after:
            try:
                self.root.after_cancel(self._stage_configure_after)
            except Exception:
                pass
        self._stage_configure_after = self.root.after(120, self._redraw_stage_debounced)

    def _redraw_stage_soon(self) -> None:
        if self.mode_var.get() == "manual":
            self._schedule_manual_stage_paint()
        else:
            self.root.after_idle(self._paint_stage)

    def _redraw_stage_debounced(self) -> None:
        self._stage_configure_after = None
        self._paint_stage()

    def _paint_stage(self) -> None:
        with span("gui.stage.paint"):
            self._paint_stage_impl()

    def _paint_stage_impl(self) -> None:
        self.stage.delete("all")
        self._stage_image_id = None
        self._stage_photo = None
        mode = self.mode_var.get()
        sw = max(self.stage.winfo_width(), 50)
        sh = max(self.stage.winfo_height(), 50)
        if mode == "manual":
            img = self._build_manual_stage_image()
            if img is not None:
                self._stage_photo = ImageTk.PhotoImage(img)
                self._stage_image_id = self.stage.create_image(
                    sw // 2,
                    sh // 2,
                    image=self._stage_photo,
                )
            self._stage_hscroll.grid_remove()
            try:
                self.stage.config(xscrollcommand=lambda *_: None)
            except tk.TclError:
                pass
            self.stage.config(scrollregion=(0, 0, sw, sh))
            self.stage.xview_moveto(0)
            self.stage.yview_moveto(0)
            return
        self._stage_hscroll.grid_remove()
        try:
            self.stage.config(xscrollcommand=lambda *_: None)
        except tk.TclError:
            pass
        self.stage.config(scrollregion=(0, 0, sw, sh))
        self.stage.xview_moveto(0)
        self.stage.yview_moveto(0)
        self._paint_auto_stage_placeholder()


    def _build_manual_stage_image(self) -> Image.Image | None:
        with span("gui.stage.build_manual_image"):
            return self._build_manual_stage_image_impl()

    def _build_manual_stage_image_impl(self) -> Image.Image | None:
        m = self._stage_metrics()
        if m is None:
            return None
        scale, ox, oy = m
        sw = max(self.stage.winfo_width(), 50)
        sh = max(self.stage.winfo_height(), 50)
        geom = get_layout_geometry(self.layout_var.get(), self.borderless_var.get(), self.bleed_var.get())
        color = parse_color(self.color_var.get())
        cw = max(1, int(round(CANVAS_WIDTH * scale)))
        ch = max(1, int(round(CANVAS_HEIGHT * scale)))
        rects = self._slot_pixel_rects(scale, 0.0, 0.0, geom)
        out = Image.new("RGB", (sw, sh), (20, 20, 20))
        canvas = Image.new("RGB", (cw, ch), color)
        draw = ImageDraw.Draw(canvas)
        orient = geom["orientation"]
        for i, (x0, y0, x1, y1) in enumerate(rects):
            tws = max(1, x1 - x0)
            ths = max(1, y1 - y0)
            fill = self._slot_assignments[i] if i < len(self._slot_assignments) else None
            if fill is not None:
                px, py = fill.pan_x, fill.pan_y
                if self._pan_drag_slot == i and self._pan_live is not None:
                    px, py = self._pan_live
                cell = process_image_preview_cell(
                    fill.path,
                    tws,
                    ths,
                    orient,
                    px,
                    py,
                    flip_h=fill.flip_h,
                    grayscale=fill.grayscale,
                )
                if cell:
                    if cell.size != (tws, ths):
                        cell = cell.resize((tws, ths), Image.Resampling.LANCZOS)
                    canvas.paste(cell, (x0, y0))
                else:
                    draw.rectangle([x0, y0, x0 + tws - 1, y0 + ths - 1], outline="#888", width=2)
                    draw.text((x0 + 4, y0 + 4), str(i + 1), fill=(200, 200, 200))
            else:
                draw.rectangle([x0 + 1, y0 + 1, x0 + tws - 2, y0 + ths - 2], outline="#666", width=2)
                draw.text((x0 + max(2, tws // 2 - 6), y0 + max(2, ths // 2 - 6)), str(i + 1), fill=(160, 160, 160))
            if self._swap_pickup_slot is not None:
                if i == self._swap_pickup_slot:
                    draw.rectangle([x0 - 2, y0 - 2, x0 + tws + 1, y0 + ths + 1], outline="#66ccff", width=2)
                elif self._swap_hover_slot == i and i != self._swap_pickup_slot:
                    draw.rectangle([x0 - 2, y0 - 2, x0 + tws + 1, y0 + ths + 1], outline="#ffaa33", width=3)
        out.paste(canvas, (int(round(ox)), int(round(oy))))
        return out

    def _paint_auto_stage_placeholder(self) -> None:
        self.stage.create_text(
            self.stage.winfo_width() // 2,
            self.stage.winfo_height() // 2,
            text="Preview loads here\n(non-manual modes)",
            fill="#888888",
            justify=tk.CENTER,
        )

    def _schedule_folder_preview(self) -> None:
        if self._folder_preview_after_id:
            try:
                self.root.after_cancel(self._folder_preview_after_id)
            except Exception:
                pass
        self._folder_preview_after_id = self.root.after(FOLDER_PREVIEW_DEBOUNCE_MS, self._folder_preview_fire)

    def _folder_preview_fire(self) -> None:
        self._folder_preview_after_id = None
        self._schedule_preview()
        self._update_run_estimate()

    def _schedule_preview(self, *, immediate: bool = False) -> None:
        if self.mode_var.get() == "manual":
            if self._preview_after_id:
                try:
                    self.root.after_cancel(self._preview_after_id)
                except Exception:
                    pass
                self._preview_after_id = None
            self._schedule_manual_stage_paint()
            return
        if self._preview_after_id:
            try:
                self.root.after_cancel(self._preview_after_id)
            except Exception:
                pass
            self._preview_after_id = None
        if immediate:
            self._start_preview_thread()
        else:
            self._preview_after_id = self.root.after(PREVIEW_DEBOUNCE_MS, self._start_preview_thread)


    def _start_preview_thread(self) -> None:
        self._preview_after_id = None
        if self._busy:
            return

        params = self._compute_preview_params()
        max_h = min(720, max(400, self.stage.winfo_height() - 20))
        max_w = min(580, max(320, self.stage.winfo_width() - 20))

        def work():
            err_msg: str | None = None
            pil_image = None
            with span("gui.preview.render_worker"):
                try:
                    if params is None:
                        err_msg = None
                    else:
                        paths, layout, borderless, bleed, color = params
                        mle = max(400, max(max_w, max_h))
                        pil_image = render_collage_preview(
                            paths, layout, borderless, color, bleed=bleed, max_long_edge=mle
                        )
                        if pil_image.width > max_w or pil_image.height > max_h:
                            pil_image.thumbnail((max_w, max_h), Image.Resampling.LANCZOS)
                except Exception as e:
                    err_msg = str(e)
                    logger.debug("Preview failed: %s", e)

            def apply():
                if err_msg:
                    self._show_stage_message(err_msg)
                elif pil_image is not None:
                    self._stage_photo = ImageTk.PhotoImage(pil_image)
                    self.stage.delete("all")
                    self._stage_image_id = self.stage.create_image(
                        self.stage.winfo_width() // 2,
                        self.stage.winfo_height() // 2,
                        image=self._stage_photo,
                    )
                else:
                    self._show_stage_message(self._preview_placeholder_message())
                self._sync_preview_nav_ui()

            self.root.after(0, apply)

        threading.Thread(target=work, daemon=True).start()


    def _show_stage_message(self, msg: str) -> None:
        sw = max(self.stage.winfo_width(), 50)
        sh = max(self.stage.winfo_height(), 50)
        self.stage.delete("all")
        self.stage.config(scrollregion=(0, 0, sw, sh))
        self.stage.xview_moveto(0)
        self.stage.yview_moveto(0)
        self.stage.create_text(
            sw // 2,
            sh // 2,
            text=msg[:280] + ("Ã¢â‚¬Â¦" if len(msg) > 280 else ""),
            fill="#aaaaaa",
            width=max(200, self.stage.winfo_width() - 40),
        )

    def _preview_placeholder_message(self) -> str:
        folder = Path(self.folder_var.get())
        mode = self.mode_var.get()
        if not folder.is_dir():
            return "Choose a valid input folder."
        if mode == "combo":
            need = LAYOUT_CONFIG["grid-2x4"]["num_images"]
            orient = LAYOUT_CONFIG["grid-2x4"]["orientation"]
            got = len(get_valid_paths(folder, orient))
            return f"Combo preview: need {need} {orient} images, found {got}."
        layout = self.layout_var.get()
        n = LAYOUT_CONFIG[layout]["num_images"]
        orient = LAYOUT_CONFIG[layout]["orientation"]
        got = len(get_valid_paths(folder, orient))
        if orient == "mixed":
            return f"Need {n} photos (H or V); found {got}."
        return f"Need {n} {orient} images; found {got}."


    def _compute_preview_params(self) -> tuple[list[Path], str, bool, bool, str] | None:
        mode = self.mode_var.get()
        folder = Path(self.folder_var.get())
        color = self.color_var.get()
        if not folder.is_dir() or mode == "manual":
            return None
        entries = self._auto_preview_entries()
        if not entries:
            return None
        total = len(entries)
        idx = max(0, min(self._auto_preview_index, total - 1))
        self._auto_preview_index = idx
        paths, layout, borderless = entries[idx]
        bleed = bool(self.bleed_var.get())
        return (paths, layout, borderless, bleed, color)


    def _validate_run(self) -> str | None:
        folder = Path(self.folder_var.get())
        if not folder.is_dir():
            return "Input folder does not exist."
        out = Path(self.output_var.get())
        try:
            out.mkdir(parents=True, exist_ok=True)
        except OSError as e:
            return f"Cannot create output folder: {e}"
        if self.mode_var.get() == "manual":
            ordered = self._manual_paths_ordered()
            if ordered is None:
                return f"Fill all {self._required_count()} slots on the preview."

        elif self.mode_var.get() != "combo" and self._expected_output_count() == 0:
            return "Nothing to generate Ã¢â‚¬â€ check photos and mode (not enough matching images?)."
        return None

    def _run_progress_report(self, done: int, total: int) -> None:
        total = max(1, total)
        self.run_progress.configure(mode="determinate", maximum=total, value=min(done, total))

    def _log_file_dir(self) -> Path:
        base = os.environ.get("LOCALAPPDATA") or str(Path.home())
        return Path(base) / "ImageStacker" / "logs"

    def _reveal_folder(self, folder: Path) -> None:
        try:
            folder = folder.expanduser().resolve()
        except OSError:
            return
        if not folder.is_dir():
            try:
                folder.mkdir(parents=True, exist_ok=True)
            except OSError:
                return
        try:
            if sys.platform == "win32":
                os.startfile(folder)  # type: ignore[attr-defined]
            elif sys.platform == "darwin":
                subprocess.run(["open", str(folder)], check=False)
            else:
                subprocess.run(["xdg-open", str(folder)], check=False)
        except OSError as e:
            logger.warning("Could not open folder %s: %s", folder, e)

    def _open_last_output_folder(self) -> None:
        if self._last_run_output_dir is None:
            return
        self._reveal_folder(self._last_run_output_dir)

    def _show_shortcuts_dialog(self) -> None:
        win = tk.Toplevel(self.root)
        win.title("Shortcuts")
        win.transient(self.root)
        txt = ScrolledText(win, width=72, height=18, wrap="word", font=("Segoe UI", 9))
        txt.pack(fill=tk.BOTH, expand=True, padx=8, pady=8)
        body = """GENERAL
- Run builds collages (see the run info line for how many).
- After a successful run, use "Open output folder".

MANUAL MODE
- Drag a thumbnail onto a slot, or click a thumb to fill the next empty slot.
- Drag on a filled photo to pan the crop (release to commit).
- Double-click a filled slot: flip horizontally.
- Shift+double-click: black & white for that slot.
- Ctrl+drag from one slot to another: swap slots.
- Right-click a slot: clear it.
- Ctrl+Z / Ctrl+Y: undo / redo slot edits (up to 50 steps).

OTHER MODES
- Single, batch, random, and combo use the input folder and layout cards.
- Preview bar arrows browse possible collages; "Use in manual" copies preview into Manual.

Log file: ImageStacker/logs/app.log
"""
        txt.insert("1.0", body.strip())
        txt.config(state="disabled")
        ttk.Button(win, text="Close", command=win.destroy).pack(pady=(0, 8))


    def _on_run(self) -> None:
        if self._busy:
            return
        err = self._validate_run()
        if err:
            messagebox.showerror("Cannot run", err)
            return
        self._busy = True
        self._run_t0 = time.monotonic()
        try:
            self.run_progress.stop()
        except tk.TclError:
            pass
        self.run_progress.grid(row=1, column=0, sticky="ew", pady=(8, 0))
        n_out = max(1, self._expected_output_count())
        self.run_progress.config(mode="determinate", maximum=n_out, value=0)
        if n_out > 1 or self.mode_var.get() in ("batch", "combo", "random"):
            lo = max(2, int(n_out * 0.2))
            hi = max(lo + 4, int(n_out * 0.9))
            self.run_working_var.set(
                f"ProcessingÃ¢â‚¬Â¦ about {lo}Ã¢â‚¬â€œ{hi}s for ~{n_out} file(s) (varies with CPU & photos)."
            )
        else:
            self.run_working_var.set("ProcessingÃ¢â‚¬Â¦")
        self.status_var.set("WorkingÃ¢â‚¬Â¦")
        folder = Path(self.folder_var.get()).resolve()
        out = Path(self.output_var.get())
        layout = self.layout_var.get()
        borderless = self.borderless_var.get()
        color = self.color_var.get()
        mode = self.mode_var.get()
        paths_manual = self._manual_paths_ordered()
        pans_manual = self._manual_pans_ordered()
        flips_manual = self._manual_flip_h_ordered()
        grays_manual = self._manual_grayscale_ordered()

        def progress_report(done: int, total: int) -> None:
            self.root.after(0, lambda d=done, t=total: self._run_progress_report(d, t))

        def work():
            exc: Exception | None = None
            try:
                if mode == "combo":
                    run_combo_job(
                        folder, out, color,
                        bleed=bool(self.bleed_var.get()),
                        progress_callback=progress_report,
                    )
                elif mode == "manual" and paths_manual and pans_manual and flips_manual and grays_manual:
                    progress_report(0, 1)
                    dest = generate_output_filename(out, "manual")
                    run_collage_from_paths(
                        paths_manual,
                        layout,
                        borderless,
                        color,
                        dest,
                        bleed=bool(self.bleed_var.get()),
                        slot_pans=pans_manual,
                        slot_flip_h=flips_manual,
                        slot_grayscale=grays_manual,
                    )
                    progress_report(1, 1)
                elif mode == "single":
                    run_layout_job(
                        str(folder),
                        str(out),
                        layout,
                        int(self.count_var.get()),
                        False,
                        False,
                        borderless,
                        color,
                        bleed=bool(self.bleed_var.get()),
                        progress_callback=progress_report,
                    )
                elif mode == "batch":
                    run_layout_job(
                        str(folder),
                        str(out),
                        layout,
                        1,
                        True,
                        False,
                        borderless,
                        color,
                        bleed=bool(self.bleed_var.get()),
                        progress_callback=progress_report,
                    )
                elif mode == "random":
                    run_layout_job(
                        str(folder),
                        str(out),
                        layout,
                        int(self.count_var.get()),
                        False,
                        True,
                        borderless,
                        color,
                        bleed=bool(self.bleed_var.get()),
                        progress_callback=progress_report,
                    )

            except Exception as e:
                exc = e
                logger.exception("Run failed")
            self.root.after(0, lambda: self._run_done(exc))

        threading.Thread(target=work, daemon=True).start()

    def _run_done(self, exc: Exception | None) -> None:
        self._busy = False
        try:
            self.run_progress.stop()
        except tk.TclError:
            pass
        try:
            self.run_progress.grid_remove()
        except tk.TclError:
            pass
        self.run_working_var.set("")
        elapsed = time.monotonic() - getattr(self, "_run_t0", time.monotonic())
        self._schedule_preview()
        out_dir = Path(self.output_var.get()).expanduser()
        try:
            out_resolved = out_dir.resolve()
        except OSError:
            out_resolved = out_dir
        if exc:
            self.status_var.set("Failed Ã¢â‚¬â€ see log.")
            self._last_run_output_dir = None
            if self._open_output_btn is not None:
                self._open_output_btn.state(["disabled"])
            msg = str(exc)[:1200]
            if messagebox.askyesno("Run failed", f"{msg}\n\nOpen log folder?"):
                self._reveal_folder(self._log_file_dir())
        else:
            self.status_var.set(f"Finished in {elapsed:.1f}s. Check output folder.")
            self._last_run_output_dir = out_resolved
            if self._open_output_btn is not None:
                self._open_output_btn.state(["!disabled"])

    def _load_settings(self) -> None:
        path = _settings_file()
        if path.is_file():
            try:
                data = json.loads(path.read_text(encoding="utf-8"))
            except Exception:
                data = {}
            else:
                if "input_folder" in data and Path(data["input_folder"]).is_dir():
                    self.folder_var.set(data["input_folder"])
                if "output_folder" in data:
                    self.output_var.set(data["output_folder"])
                if data.get("layout") in LAYOUT_CONFIG:
                    self.layout_var.set(data["layout"])
                if data.get("mode") in ("single", "batch", "random", "combo", "manual"):
                    self.mode_var.set(data["mode"])
                if "count" in data:
                    try:
                        self.count_var.set(int(data["count"]))
                    except (TypeError, ValueError):
                        pass
                if "borderless" in data:
                    self.borderless_var.set(bool(data["borderless"]))
                if "bleed" in data:
                    self.bleed_var.set(bool(data["bleed"]))
                if "color" in data:
                    self.color_var.set(str(data["color"]))
        self._sync_color_button_text()
        self._highlight_layout_card()
        self._on_mode_change()
        self._schedule_preview()
        self._redraw_stage_soon()
        self._update_run_estimate(immediate=True)

    def _save_settings(self) -> None:
        data = {
            "input_folder": self.folder_var.get(),
            "output_folder": self.output_var.get(),
            "layout": self.layout_var.get(),
            "mode": self.mode_var.get(),
            "count": int(self.count_var.get()),
            "borderless": bool(self.borderless_var.get()),
            "bleed": bool(self.bleed_var.get()),
            "color": self.color_var.get(),
        }
        try:
            _settings_file().write_text(json.dumps(data, indent=2), encoding="utf-8")
        except OSError as e:
            logger.warning("Could not save settings: %s", e)

    def _on_close(self) -> None:
        self._save_settings()
        self.root.destroy()


