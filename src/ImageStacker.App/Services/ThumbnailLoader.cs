using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageStacker.App.Imaging;
using ImageStacker.Core;

namespace ImageStacker.App.Services;

internal sealed class ThumbnailItem : INotifyPropertyChanged
{
    private bool _included = true;

    public required string Path { get; init; }
    public WriteableBitmap? Bitmap { get; set; }
    public string FileName => System.IO.Path.GetFileName(Path);

    public bool Included
    {
        get => _included;
        set
        {
            if (_included == value)
            {
                return;
            }

            _included = value;
            OnPropertyChanged();
            IncludedChanged?.Invoke(this);
        }
    }

    public Action<ThumbnailItem>? IncludedChanged { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SyncIncluded(bool included)
    {
        if (_included == included)
        {
            return;
        }

        _included = included;
        OnPropertyChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class ThumbnailLoader : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;

    public ThumbnailLoader(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public event Action<IReadOnlyList<ThumbnailItem>>? ThumbnailsUpdated;

    public void LoadFolder(string folder)
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _ = LoadFolderAsync(folder, _cts.Token);
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _cts?.Cancel();
        }
    }

    private async Task LoadFolderAsync(string folder, CancellationToken token)
    {
        var items = new List<ThumbnailItem>();
        Publish(items);

        if (!Directory.Exists(folder))
        {
            return;
        }

        List<string> paths = Directory
            .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(p => Constants.ImageExtensions.Contains(System.IO.Path.GetExtension(p)))
            .Select(System.IO.Path.GetFullPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Take(UiConstants.ThumbDisplayCap)
            .ToList();

        foreach (string path in paths)
        {
            items.Add(new ThumbnailItem { Path = path });
        }

        Publish(items);

        int index = 0;
        while (index < paths.Count)
        {
            token.ThrowIfCancellationRequested();

            int batchEnd = Math.Min(index + UiConstants.ThumbBatchSize, paths.Count);
            var batchPaths = paths.GetRange(index, batchEnd - index);

            Dictionary<string, BitmapBuffer> decoded = await Task.Run(
                () => DecodeBatch(batchPaths, token),
                token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            await _dispatcher.InvokeAsync(() =>
            {
                foreach (string path in batchPaths)
                {
                    ThumbnailItem? item = items.FirstOrDefault(i => i.Path == path);
                    if (item is not null && decoded.TryGetValue(path, out BitmapBuffer buffer))
                    {
                        item.Bitmap = VipsBitmapConverter.BufferToWriteableBitmap(buffer);
                    }
                }

                Publish(items);
            }, DispatcherPriority.Background);

            index = batchEnd;
            await Task.Delay(20, token).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, BitmapBuffer> DecodeBatch(
        IReadOnlyList<string> paths,
        CancellationToken token)
    {
        var result = new Dictionary<string, BitmapBuffer>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                result[path] = VipsBitmapConverter.CreateThumbTileBuffer(
                    path,
                    UiConstants.ThumbTileWidth,
                    UiConstants.ThumbTileHeight);
            }
            catch (Exception ex)
            {
                FileLogger.Warning($"Thumbnail failed for {path}: {ex.Message}");
            }
        }

        return result;
    }

    private void Publish(IReadOnlyList<ThumbnailItem> items)
    {
        _dispatcher.Invoke(() => ThumbnailsUpdated?.Invoke(items.ToList()));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
