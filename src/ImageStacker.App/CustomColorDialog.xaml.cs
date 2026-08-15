using System.Windows;

namespace ImageStacker.App;

public partial class CustomColorDialog : Window
{
    public string ColorValue => ColorBox.Text.Trim();

    public CustomColorDialog(string initial)
    {
        InitializeComponent();
        ColorBox.Text = initial;
        ColorBox.SelectAll();
        ColorBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ColorBox.Text))
        {
            MessageBox.Show("Enter a color name or #RRGGBB value.", "Custom color",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
