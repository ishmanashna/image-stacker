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
            - Run exports collages (see the run info line for current vs all ticked when a deck is shown).
            - After a successful run, use "Open output folder".

            STAGE EDITING (any mode with a collage on stage)
            - Drag a thumbnail onto a slot, or click a thumb to fill the next empty slot.
            - Any photo can go in any slot \u2014 cover-crop fills the cell (portrait in landscape slots, etc.).
            - Drag on a filled photo to pan the crop (release to commit).
            - Double-click a filled slot: flip horizontally.
            - Shift+double-click: black & white for that slot.
            - Ctrl+drag from one slot to another: swap slots.
            - Right-click a slot: clear it.
            - Ctrl+Z / Ctrl+Y: undo / redo slot edits (up to 50 steps, per collage).

            BLANK COLLAGE MODE
            - Starts an empty collage for the selected layout \u2014 same gestures as editing a generated card.

            OTHER MODES
            - Single, batch, random, and combo build collages from the input folder.
            - Focus a deck card or use \u25C0 \u25B6 to edit that collage on the stage without switching mode.
            - With a deck, Run asks export current (focused card) or export all ticked cards.

            Log file: %LOCALAPPDATA%\ImageStacker\logs\app.log
            """.Trim();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
