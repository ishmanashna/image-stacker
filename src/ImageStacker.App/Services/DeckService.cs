using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageStacker.App.Imaging;
using ImageStacker.Core.Contracts;
using ImageStacker.Core.Jobs;
using NetVips;

namespace ImageStacker.App.Services;

internal sealed class DeckService : IDisposable
{
    private const int PreviewLongEdge = 480;
    private const int MaxConcurrentPreviews = 2;

    private readonly Dispatcher _dispatcher;
    private readonly object _previewLock = new();
    private CancellationTokenSource? _previewCts;
    private int _previewGeneration;
    private string _color = "white";
    private bool _bleed;

    public DeckService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public IReadOnlyList<DeckCardItem> Cards { get; private set; } = Array.Empty<DeckCardItem>();

    public int FocusIndex { get; private set; }

    public event Action<int>? FocusIndexChanged;
    public event Action? SelectionChanged;

    public static bool ShouldShowDeck(string mode, int jobCount) =>
        mode is "combo" or "batch" or "random" && jobCount > 1;

    /// <summary>
    /// Refreshes deck previews when only color/bleed changed, or rebuilds card identities
    /// when the candidate list changed. Returns true when card identities were rebuilt.
    /// </summary>
    public bool RefreshOrRebuild(
        string inputFolder,
        string mode,
        string layout,
        int count,
        bool borderless,
        string color,
        bool bleed)
    {
        IReadOnlyList<ExportJob> jobs = ExportService.BuildJobs(inputFolder, mode, layout, count, borderless);

        if (CandidatesMatch(Cards, jobs))
        {
            SoftRefresh(color, bleed);
            return false;
        }

        HardRebuild(jobs, color, bleed);
        return true;
    }

    public void Clear()
    {
        CancelPreviews();
        Cards = Array.Empty<DeckCardItem>();
        FocusIndex = 0;
    }

    public int SelectedCount => Cards.Count(c => c.IsSelected);

    public void SelectAll()
    {
        foreach (DeckCardItem card in Cards)
        {
            card.IsSelected = true;
        }

        SelectionChanged?.Invoke();
    }

    public void SelectNone()
    {
        foreach (DeckCardItem card in Cards)
        {
            card.IsSelected = false;
        }

        SelectionChanged?.Invoke();
    }

    public void NotifySelectionChanged() => SelectionChanged?.Invoke();

    public void SetFocusIndex(int index)
    {
        if (Cards.Count == 0)
        {
            return;
        }

        int clamped = Math.Clamp(index, 0, Cards.Count - 1);
        if (clamped == FocusIndex)
        {
            RequestPreviewForIndex(clamped);
            return;
        }

        FocusIndex = clamped;
        UpdateFocusFlags();
        FocusIndexChanged?.Invoke(FocusIndex);
        RequestPreviewForIndex(clamped);
    }

    public void RequestPreviewsForIndices(IEnumerable<int> indices)
    {
        foreach (int index in indices)
        {
            RequestPreviewForIndex(index);
        }
    }

    public void RequestPreviewForIndex(int index)
    {
        if (index < 0 || index >= Cards.Count)
        {
            return;
        }

        DeckCardItem card = Cards[index];
        if (card.PreviewRequested || card.HasPreview)
        {
            return;
        }

        card.PreviewRequested = true;
        card.PreviewLoading = true;
        QueuePreview(card);
    }

    /// <summary>
    /// Drop a card's cached thumbnail and rebuild it from current slot edits.
    /// </summary>
    public void InvalidatePreviewForIndex(int index)
    {
        if (index < 0 || index >= Cards.Count)
        {
            return;
        }

        DeckCardItem card = Cards[index];
        card.ClearPreviewState();
        RequestPreviewForIndex(index);
    }

    private void SoftRefresh(string color, bool bleed)
    {
        CancelPreviews();
        _color = color;
        _bleed = bleed;

        foreach (DeckCardItem card in Cards)
        {
            card.ClearPreviewState();
        }
    }

    private void HardRebuild(IReadOnlyList<ExportJob> jobs, string color, bool bleed)
    {
        CancelPreviews();
        _color = color;
        _bleed = bleed;

        var cards = jobs
            .Select(job => new DeckCardItem(job))
            .ToList();

        Cards = cards;
        FocusIndex = 0;
        UpdateFocusFlags();
    }

    private static bool CandidatesMatch(IReadOnlyList<DeckCardItem> cards, IReadOnlyList<ExportJob> jobs)
    {
        if (cards.Count != jobs.Count)
        {
            return false;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            ExportJob job = jobs[i];
            if (!cards[i].MatchesJob(job.JobIndex, job.LayoutName, job.Borderless, job.Paths))
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateFocusFlags()
    {
        for (int i = 0; i < Cards.Count; i++)
        {
            Cards[i].IsFocused = i == FocusIndex;
        }
    }

    private void QueuePreview(DeckCardItem card)
    {
        CancellationToken token;
        int generation;
        lock (_previewLock)
        {
            _previewCts ??= new CancellationTokenSource();
            token = _previewCts.Token;
            generation = _previewGeneration;
        }

        string color = _color;
        bool bleed = _bleed;

        _ = Task.Run(async () =>
        {
            await PreviewSemaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                using Image preview = LayoutContracts.RenderManualPreview(
                    card.LayoutName,
                    card.Borderless,
                    color,
                    bleed,
                    card.Collage.Slots,
                    PreviewLongEdge);
                BitmapBuffer buffer = VipsBitmapConverter.ImageToBuffer(preview);

                if (token.IsCancellationRequested || generation != _previewGeneration)
                {
                    return;
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || generation != _previewGeneration)
                    {
                        return;
                    }

                    card.PreviewBitmap = VipsBitmapConverter.BufferToWriteableBitmap(buffer);
                    card.PreviewLoading = false;
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    if (generation == _previewGeneration)
                    {
                        card.PreviewLoading = false;
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                FileLogger.Warning($"Deck preview failed for card {card.DeckIndex}: {ex.Message}");
                await _dispatcher.InvokeAsync(() =>
                {
                    if (generation != _previewGeneration)
                    {
                        return;
                    }

                    card.PreviewLoading = false;
                    card.PreviewRequested = false;
                }, DispatcherPriority.Background);
            }
            finally
            {
                PreviewSemaphore.Release();
            }
        }, token);
    }

    private static readonly SemaphoreSlim PreviewSemaphore = new(MaxConcurrentPreviews, MaxConcurrentPreviews);

    private void CancelPreviews()
    {
        lock (_previewLock)
        {
            _previewGeneration++;
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = null;
        }
    }

    public void Dispose() => CancelPreviews();
}
