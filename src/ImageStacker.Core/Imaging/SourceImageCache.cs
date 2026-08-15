using System.Collections.Concurrent;
using NetVips;

namespace ImageStacker.Core.Imaging;

public sealed class SourceImageCache : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<NetVips.Image>> _cache = new(StringComparer.OrdinalIgnoreCase);
    // Serializes cached-image access + Copy(); NetVips images are not safe for concurrent use on one handle.
    private readonly object _copyLock = new();
    private int _disposed;

    /// <summary>
    /// Returns an independent copy of the decoded source. Lock covers get+Copy so parallel export
    /// workers never share or race on the same cached NetVips.Image handle.
    /// </summary>
    public NetVips.Image GetCopy(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        string fullPath = Path.GetFullPath(path);
        lock (_copyLock)
        {
            Lazy<NetVips.Image> entry = _cache.GetOrAdd(
                fullPath,
                p => new Lazy<NetVips.Image>(() => LoadImage(p), LazyThreadSafetyMode.ExecutionAndPublication));
            return entry.Value.Copy();
        }
    }

    private static NetVips.Image LoadImage(string path)
    {
        return NetVips.Image.NewFromFile(path, access: Enums.Access.Random).Autorot();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_copyLock)
        {
            foreach (var entry in _cache.Values)
            {
                if (entry.IsValueCreated)
                {
                    entry.Value.Dispose();
                }
            }

            _cache.Clear();
        }
    }
}
