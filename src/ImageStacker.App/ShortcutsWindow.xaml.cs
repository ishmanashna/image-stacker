using System.Windows;

namespace ImageStacker.App;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
        InitializeComponent();
        ShortcutsText.Text =
            """
            GENERAL
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
            - Left / Right arrow keys (or ◀ ▶) browse preview candidates.
            - "Use in manual" copies the current preview into Manual slots (pan defaults for grid-1x2-v).

            Log file: %LOCALAPPDATA%\ImageStacker\logs\app.log
            """.Trim();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
