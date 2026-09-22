using System.Windows;

namespace ImageStacker.App;

public enum ExportChoice
{
    Current,
    All,
}

public partial class ExportChoiceDialog : Window
{
    public ExportChoice Choice { get; private set; }

    public ExportChoiceDialog(int tickedCount)
    {
        InitializeComponent();
        SubtitleText.Text =
            $"Export current writes the focused card only. Export all writes {tickedCount} ticked card(s) with their current crops.";
        CurrentButton.Focus();
    }

    private void Current_Click(object sender, RoutedEventArgs e)
    {
        Choice = ExportChoice.Current;
        DialogResult = true;
    }

    private void All_Click(object sender, RoutedEventArgs e)
    {
        Choice = ExportChoice.All;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
