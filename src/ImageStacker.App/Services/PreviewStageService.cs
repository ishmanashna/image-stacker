using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageStacker.App.Imaging;
using ImageStacker.Core;
using ImageStacker.Core.Contracts;
using ImageStacker.Core.Layout;
using NetVips;

namespace ImageStacker.App.Services;

internal sealed class PreviewStageState
{
    public WriteableBitmap? Bitmap { get; init; }
    public string NavLabel { get; init; } = "—";
    public bool CanPrev { get; init; }
    public bool CanNext { get; init; }
    public string EmptyMessage { get; init; } = string.Empty;
    public bool IsEmpty { get; init; }
}

internal sealed class ManualLiveOverrides
{
    public int? LivePanSlot { get; init; }
    public double LivePanX { get; init; }
    public double LivePanY { get; init; }
}

internal sealed class PreviewStageService : IDisposable
{
    private const int DebounceMs = 280;
    private const int EditDebounceMs = 48;
    private const int PanDebounceMs = 32;
    private const int PreviewLongEdge = 1000;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounceTimer;
    private CancellationTokenSource? _renderCts;
    private int _focusIndex;
    private PreviewRequest? _pendingRequest;
    private ManualLiveOverrides? _manualLive;
    private bool _fastDebounce;

    public PreviewStageService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(DebounceMs),
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            _ = RenderCurrentAsync();
        };
    }

    public event Action<PreviewStageState>? StageUpdated;

    public event Action<int>? FocusIndexChanged;

    public void ScheduleRefresh(PreviewRequest request, bool immediate = false, int? initialIndex = null)
    {
        _pendingRequest = request;
        if (initialIndex is int index)
        {
            _focusIndex = Math.Clamp(index, 0, Math.Max(0, request.FocusCount - 1));
        }
        else
        {
            _focusIndex = Math.Clamp(request.FocusIndex, 0, Math.Max(0, request.FocusCount - 1));
        }

        int debounce = _fastDebounce ? PanDebounceMs : immediate ? 0 : EditDebounceMs;
        _fastDebounce = false;
        if (!immediate && request.Mode != "manual")
        {
            debounce = DebounceMs;
        }

        _debounceTimer.Interval = TimeSpan.FromMilliseconds(debounce);
        _debounceTimer.Stop();
        if (immediate || debounce == 0)
        {
            _ = RenderCurrentAsync();
        }
        else
        {
            _debounceTimer.Start();
        }
    }

    public void SetManualLivePan(int slot, double panX, double panY)
    {
        _manualLive = new ManualLiveOverrides
        {
            LivePanSlot = slot,
            LivePanX = panX,
            LivePanY = panY,
        };
        SchedulePanRefresh();
    }

    private void SchedulePanRefresh()
    {
        if (_pendingRequest is null)
        {
            throw new InvalidOperationException("No preview request.");
        }

        _fastDebounce = true;
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(PanDebounceMs);
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void ClearManualLivePan()
    {
        _manualLive = null;
    }

    public void FlushPendingRender()
    {
        if (_pendingRequest is null)
        {
            return;
        }

        _debounceTimer.Stop();
        _ = RenderCurrentAsync();
    }

    public int FocusIndex => _focusIndex;

    public int FocusCount => _pendingRequest?.FocusCount ?? 0;

    public void SetFocusIndex(int index)
    {
        if (_pendingRequest is null || _pendingRequest.FocusCount == 0)
        {
            return;
        }

        int clamped = Math.Clamp(index, 0, _pendingRequest.FocusCount - 1);
        if (clamped == _focusIndex)
        {
            return;
        }

        _focusIndex = clamped;
        FocusIndexChanged?.Invoke(_focusIndex);
    }

    public void MovePrevious()
    {
        if (_focusIndex > 0)
        {
            SetFocusIndex(_focusIndex - 1);
        }
    }

    public void MoveNext()
    {
        if (_pendingRequest is not null && _focusIndex < _pendingRequest.FocusCount - 1)
        {
            SetFocusIndex(_focusIndex + 1);
        }
    }

    public void Dispose()
    {
        _debounceTimer.Stop();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
    }

    private async Task RenderCurrentAsync()
    {
        PreviewRequest? request = _pendingRequest;
        if (request is null)
        {
            return;
        }

        _renderCts?.Cancel();
        _renderCts?.Dispose();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        CancellationToken token = cts.Token;

        EditableCollage? collage = request.FocusedCollage;
        if (collage is null)
        {
            PublishEmpty(BuildEmptyMessage(request));
            return;
        }

        int filled = collage.Slots.Count(s => s is not null);
        if (request.Mode == "manual" && filled == 0)
        {
            PublishEmpty(BuildManualEmptyMessage(collage));
            return;
        }

        if (collage.Slots.Count == 0)
        {
            PublishEmpty(BuildEmptyMessage(request));
            return;
        }

        int focusIndex = Math.Clamp(_focusIndex, 0, Math.Max(0, request.FocusCount - 1));
        string navLabel = request.Mode == "manual"
            ? $"Blank · {filled}/{collage.Slots.Count} filled"
            : request.FocusCount > 1
                ? $"{focusIndex + 1} / {request.FocusCount}"
                : collage.Layout;
        bool canPrev = request.Mode != "manual" && focusIndex > 0;
        bool canNext = request.Mode != "manual" && focusIndex < request.FocusCount - 1;

        try
        {
            ManualLiveOverrides? live = _manualLive;
            BitmapBuffer buffer = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using NetVips.Image preview = LayoutContracts.RenderManualPreview(
                    collage.Layout,
                    collage.Borderless,
                    request.Color,
                    request.Bleed,
                    collage.Slots,
                    PreviewLongEdge,
                    live?.LivePanSlot,
                    live?.LivePanX,
                    live?.LivePanY);
                return VipsBitmapConverter.ImageToBuffer(preview);
            }, token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            WriteableBitmap bitmap = VipsBitmapConverter.BufferToWriteableBitmap(buffer);
            Publish(new PreviewStageState
            {
                Bitmap = bitmap,
                NavLabel = navLabel,
                CanPrev = canPrev,
                CanNext = canNext,
                IsEmpty = false,
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer preview request.
        }
        catch (Exception ex)
        {
            FileLogger.Warning($"Preview render failed: {ex.Message}");
            PublishEmpty($"Preview failed: {ex.Message}");
        }
    }

    private static string BuildEmptyMessage(PreviewRequest request)
    {
        if (request.Mode == "manual")
        {
            return BuildManualEmptyMessage(null);
        }

        if (string.IsNullOrWhiteSpace(request.InputFolder) || !Directory.Exists(request.InputFolder))
        {
            return "Choose an input folder to preview collages.";
        }

        return "Not enough matching photos for this layout and mode.";
    }

    private static string BuildManualEmptyMessage(EditableCollage? collage)
    {
        int required = collage is null
            ? LayoutCatalog.GetRequired("stack-3").NumImages
            : collage.Slots.Count;
        return $"Blank collage: fill {required} slot(s).\nDrag or click thumbnails onto the stage.";
    }

    private void PublishEmpty(string message)
    {
        Publish(new PreviewStageState
        {
            NavLabel = "—",
            CanPrev = false,
            CanNext = false,
            IsEmpty = true,
            EmptyMessage = message,
        });
    }

    private void Publish(PreviewStageState state) => StageUpdated?.Invoke(state);
}

internal sealed record PreviewRequest(
    string InputFolder,
    string Mode,
    string Layout,
    int Count,
    bool Borderless,
    bool Bleed,
    string Color,
    EditableCollage? FocusedCollage,
    int FocusIndex,
    int FocusCount);
