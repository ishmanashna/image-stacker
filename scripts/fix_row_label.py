from pathlib import Path
import re

p = Path(__file__).resolve().parents[1] / "app" / "gui" / "main_window.py"
text = p.read_text(encoding="utf-8")
text = re.sub(
    r'"grid-1x3-m": \("[^"]+",',
    '"grid-1x3-m": ("Row 1x3",',
    text,
    count=1,
)
p.write_text(text, encoding="utf-8")
print("Updated", p)
