using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageStacker.App.Imaging;
using ImageStacker.Core;
using ImageStacker.Core.Contracts;
using ImageStacker.Core.Layout;
using NetVips;

namespace ImageStacker.App.Services;

internal sealed record PreviewCandidate(
    IReadOnlyList<string> Paths,
    string LayoutName,
    bool Borderless);

internal sealed class PreviewStageState
{
    public WriteableBitmap? Bitmap { get; init; }
    public string NavLabel { get; init; } = "—";
    public bool CanPrev { get; init; }
    public bool CanNext { get; init; }
    public bool CanUseManual { get; init; }
    public string EmptyMessage { get; init; } = string.Empty;
    public bool IsEmpty { get; init; }
    public PreviewCandidate? CurrentCandidate { get; init; }
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
    private const int ManualDebounceMs = 48;
    private const int ManualPanDebounceMs = 32;
    private const int PreviewLongEdge = 1000;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounceTimer;
    private CancellationTokenSource? _renderCts;
    private int _candidateIndex;
    private IReadOnlyList<PreviewCandidate> _candidates = Array.Empty<PreviewCandidate>();
    private PreviewRequest? _pendingRequest;
    private ManualLiveOverrides? _manualLive;

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

    public void ScheduleRefresh(PreviewRequest request, bool immediate = false, int? initialIndex = null)
    {
        _pendingRequest = request;
        if (request.Mode == "manual")
        {
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(immediate ? 0 : ManualDebounceMs);
            _debounceTimer.Stop();
            if (immediate)
            {
                _ = RenderCurrentAsync();
            }
            else
            {
                _debounceTimer.Start();
            }

            return;
        }

        _manualLive = null;
        RebuildCandidates(request);
        if (initialIndex is int index)
        {
            _candidateIndex = Math.Clamp(index, 0, Math.Max(0, _candidates.Count - 1));
        }
        else
        {
            _candidateIndex = Math.Clamp(_candidateIndex, 0, Math.Max(0, _candidates.Count - 1));
        }

        _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceMs);
        _debounceTimer.Stop();
        if (immediate)
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
        ScheduleManualPanRefresh();
    }

    private void ScheduleManualPanRefresh()
    {
        if (_pendingRequest is null)
        {
            throw new InvalidOperationException("No preview request.");
        }

        _debounceTimer.Interval = TimeSpan.FromMilliseconds(ManualPanDebounceMs);
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

    public int CandidateIndex => _candidateIndex;

    public int CandidateCount => _candidates.Count;

    public void SetCandidateIndex(int index)
    {
        if (_candidates.Count == 0)
        {
            return;
        }

        int clamped = Math.Clamp(index, 0, _candidates.Count - 1);
        if (clamped == _candidateIndex)
        {
            return;
        }

        _candidateIndex = clamped;
        _ = RenderCurrentAsync();
    }

    public void MovePrevious()
    {
        if (_candidateIndex > 0)
        {
            SetCandidateIndex(_candidateIndex - 1);
        }
    }

    public void MoveNext()
    {
        if (_candidateIndex < _candidates.Count - 1)
        {
            SetCandidateIndex(_candidateIndex + 1);
        }
    }

    public PreviewCandidate? GetCurrentCandidate() =>
        _candidates.Count == 0 || _candidateIndex < 0 || _candidateIndex >= _candidates.Count
            ? null
            : _candidates[_candidateIndex];

    public void Dispose()
    {
        _debounceTimer.Stop();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
    }

    private void RebuildCandidates(PreviewRequest request)
    {
        if (request.Mode == "manual")
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.InputFolder) || !Directory.Exists(request.InputFolder))
        {
            _candidates = Array.Empty<PreviewCandidate>();
            return;
        }

        if (request.Mode == "combo")
        {
            _candidates = LayoutContracts.ListComboSequences(request.InputFolder)
                .Select(seq => new PreviewCandidate(seq.Paths, seq.LayoutName, seq.Borderless))
                .ToList();
            return;
        }

        IReadOnlyList<IReadOnlyList<string>> paths = LayoutContracts.ListLayoutCandidates(
            request.InputFolder,
            request.Layout,
            request.Count,
            request.Mode == "batch",
            request.Mode == "random",
            request.Borderless);

        _candidates = paths
            .Select(p => new PreviewCandidate(p, request.Layout, request.Borderless))
            .ToList();
    }

    private async Task RenderCurrentAsync()
    {
        PreviewRequest? request = _pendingRequest;
        if (request is null)
        {
            return;
        }

        if (request.Mode == "manual")
        {
            await RenderManualAsync(request).ConfigureAwait(true);
            return;
        }

        _renderCts?.Cancel();
        _renderCts?.Dispose();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        CancellationToken token = cts.Token;

        if (_candidates.Count == 0)
        {
            PublishEmpty(BuildEmptyMessage(request));
            return;
        }

        PreviewCandidate candidate = _candidates[_candidateIndex];
        string navLabel = $"{_candidateIndex + 1} / {_candidates.Count}";
        bool canPrev = _candidateIndex > 0;
        bool canNext = _candidateIndex < _candidates.Count - 1;
        bool canUseManual = request.Mode != "manual";

        try
        {
            BitmapBuffer buffer = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using NetVips.Image preview = LayoutContracts.RenderPreview(
                    candidate.Paths,
                    candidate.LayoutName,
                    candidate.Borderless,
                    request.Color,
                    request.Bleed,
                    null,
                    PreviewLongEdge);
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
                CanUseManual = canUseManual,
                IsEmpty = false,
                CurrentCandidate = candidate,
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

    private async Task RenderManualAsync(PreviewRequest request)
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        CancellationToken token = cts.Token;

        IReadOnlyList<SlotAssignment?> slots = request.ManualSlots ?? Array.Empty<SlotAssignment?>();
        int filled = slots.Count(s => s is not null);
        if (slots.Count == 0 || filled == 0)
        {
            PublishEmpty(BuildManualEmptyMessage(request));
            return;
        }

        string navLabel = $"Manual · {filled}/{slots.Count} filled";

        try
        {
            ManualLiveOverrides? live = _manualLive;
            BitmapBuffer buffer = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using NetVips.Image preview = LayoutContracts.RenderManualPreview(
                    request.ManualLayout,
                    request.Borderless,
                    request.Color,
                    request.Bleed,
                    slots,
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
                CanPrev = false,
                CanNext = false,
                CanUseManual = false,
                IsEmpty = false,
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded.
        }
        catch (Exception ex)
        {
            FileLogger.Warning($"Manual preview render failed: {ex.Message}");
            PublishEmpty($"Preview failed: {ex.Message}");
        }
    }

    private static string BuildEmptyMessage(PreviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputFolder) || !Directory.Exists(request.InputFolder))
        {
            return "Choose an input folder to preview collages.";
        }

        return "Not enough photos of the required orientation for this layout and mode.";
    }

    private static string BuildManualEmptyMessage(PreviewRequest request)
    {
        int required = LayoutCatalog.GetRequired(request.ManualLayout).NumImages;
        return $"Manual mode: fill {required} slot(s).\nDrag or click thumbnails onto the stage.";
    }

    private void PublishEmpty(string message)
    {
        Publish(new PreviewStageState
        {
            NavLabel = "—",
            CanPrev = false,
            CanNext = false,
            CanUseManual = false,
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
    string ManualLayout,
    IReadOnlyList<SlotAssignment?>? ManualSlots);
