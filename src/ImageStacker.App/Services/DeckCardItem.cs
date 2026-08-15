using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ImageStacker.App.Services;

internal sealed class DeckCardItem : INotifyPropertyChanged
{
    public DeckCardItem(
        int deckIndex,
        string layoutName,
        bool borderless,
        IReadOnlyList<string> paths)
    {
        DeckIndex = deckIndex;
        LayoutName = layoutName;
        Borderless = borderless;
        Paths = paths;
    }

    public int DeckIndex { get; }
    public string LayoutName { get; }
    public bool Borderless { get; }
    public IReadOnlyList<string> Paths { get; }

    public string Label => $"{DeckIndex}: {LayoutName}";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    private bool _isFocused;
    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused == value)
            {
                return;
            }

            _isFocused = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    public string BorderBrush => _isFocused ? "#66CCFF" : "#444";

    private WriteableBitmap? _previewBitmap;
    public WriteableBitmap? PreviewBitmap
    {
        get => _previewBitmap;
        set
        {
            if (ReferenceEquals(_previewBitmap, value))
            {
                return;
            }

            _previewBitmap = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreview));
        }
    }

    public bool HasPreview => _previewBitmap is not null;

    private bool _previewLoading;
    public bool PreviewLoading
    {
        get => _previewLoading;
        set
        {
            if (_previewLoading == value)
            {
                return;
            }

            _previewLoading = value;
            OnPropertyChanged();
        }
    }

    internal bool PreviewRequested { get; set; }

    internal void ClearPreviewState()
    {
        PreviewBitmap = null;
        PreviewRequested = false;
        PreviewLoading = false;
    }

    internal bool MatchesJob(int jobIndex, string layoutName, bool borderless, IReadOnlyList<string> paths) =>
        DeckIndex == jobIndex &&
        LayoutName == layoutName &&
        Borderless == borderless &&
        Paths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
