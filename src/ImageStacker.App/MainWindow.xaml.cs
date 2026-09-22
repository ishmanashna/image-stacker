using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IOPath = System.IO.Path;
using ImageStacker.App.Services;
using ImageStacker.Core;
using ImageStacker.Core.Export;
using ImageStacker.Core.Imaging;
using ImageStacker.Core.Jobs;
using ImageStacker.Core.Layout;
using Microsoft.Win32;

namespace ImageStacker.App;

public partial class MainWindow : Window
{
    private const double PanDragThresholdSq = 36.0;

    private readonly ThumbnailLoader _thumbnailLoader;
    private readonly PreviewStageService _previewStage;
    private readonly DeckService _deckService;
    private readonly ManualUndoStack _manualUndo = new();
    private readonly Dictionary<string, Button> _layoutButtons = new(StringComparer.OrdinalIgnoreCase);
    private bool _busy;
    private bool _suppressUiEvents = true; // true until ctor finishes (XAML Checked/TextChanged fire early)
    private bool _uiReady;
    private string? _lastRunOutputDir;

    private EditableCollage _manualCollage = EditableCollage.Blank("stack-3", false);
    private List<EditableCollage> _standaloneCollages = [];
    private List<ManualUndoStack> _standaloneUndos = [];

    private string? _thumbDragPath;
    private Point _thumbDragStart;
    private bool _thumbDragMoved;
    private bool _syncingDeckFocus;

    private int? _stageDownSlot;
    private Point _stagePressPoint;
    private bool _stagePanMoved;
    private int? _panDragSlot;
    private (double X, double Y)? _panAnchor;
    private (double X, double Y)? _panLive;
    private int? _swapPickupSlot;
    private int? _swapHoverSlot;

    public MainWindow()
    {
        InitializeComponent();
        _thumbnailLoader = new ThumbnailLoader(Dispatcher);
        _thumbnailLoader.ThumbnailsUpdated += OnThumbnailsUpdated;
        _previewStage = new PreviewStageService(Dispatcher);
        _previewStage.StageUpdated += OnPreviewStageUpdated;
        _previewStage.FocusIndexChanged += OnPreviewFocusIndexChanged;
        _deckService = new DeckService(Dispatcher);
        _deckService.FocusIndexChanged += OnDeckFocusIndexChanged;
        _deckService.SelectionChanged += OnDeckSelectionChanged;

        BuildLayoutCards();
        BuildColorCombo();
        LoadSettingsIntoUi();
        EnsureManualCollageForLayout(SelectedLayout);
        _uiReady = true;
        _suppressUiEvents = false;
        UpdateModeUi();
        UpdateRunEstimate();
        ReloadThumbnails();
        SchedulePreviewRefresh();
        DeckList.Loaded += (_, _) => HookDeckScrollViewer();
    }

    private ScrollViewer? _deckScrollViewer;

    private void HookDeckScrollViewer()
    {
        _deckScrollViewer = FindVisualChild<ScrollViewer>(DeckList);
        if (_deckScrollViewer is not null)
        {
            _deckScrollViewer.ScrollChanged += (_, _) => RequestVisibleDeckPreviews();
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            T? nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void BuildLayoutCards()
    {
        foreach (string layoutKey in UiConstants.LayoutOrder)
        {
            if (!UiConstants.LayoutCardText.TryGetValue(layoutKey, out var text))
            {
                text = (layoutKey, string.Empty);
            }

            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = $"{text.Title}\n{text.Subtitle}",
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                },
                Margin = new Thickness(2),
                Padding = new Thickness(4, 6, 4, 6),
                Background = (Brush)FindResource("LayoutCardBrush"),
                Foreground = (Brush)FindResource("LayoutCardForegroundBrush"),
                Tag = layoutKey,
            };
            button.Click += LayoutCard_Click;
            _layoutButtons[layoutKey] = button;
            LayoutGrid.Children.Add(button);
        }
    }

    private void BuildColorCombo()
    {
        ColorCombo.Items.Clear();
        foreach ((string label, string value) in UiConstants.ColorPresets)
        {
            ColorCombo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        ColorCombo.Items.Add(new ComboBoxItem { Content = "Custom…", Tag = "__custom__" });
    }

    private string SelectedLayout
    {
        get => _layoutButtons.FirstOrDefault(kv => IsLayoutSelected(kv.Value)).Key ?? "stack-3";
        set => HighlightLayoutCard(value);
    }

    private static bool IsLayoutSelected(Button button) =>
        button.Background is SolidColorBrush brush &&
        brush.Color == Color.FromRgb(0x3D, 0x6E, 0xA5);

    private void HighlightLayoutCard(string layoutKey)
    {
        foreach ((string key, Button button) in _layoutButtons)
        {
            bool selected = string.Equals(key, layoutKey, StringComparison.OrdinalIgnoreCase);
            button.Background = selected
                ? (Brush)FindResource("LayoutCardSelectedBrush")
                : (Brush)FindResource("LayoutCardBrush");
        }
    }

    private string SelectedMode
    {
        get
        {
            // During InitializeComponent, Checked handlers can fire before all radio fields exist.
            if (ModeBatch is null || ModeRandom is null || ModeCombo is null || ModeManual is null)
            {
                return "single";
            }

            if (ModeBatch.IsChecked == true)
            {
                return "batch";
            }

            if (ModeRandom.IsChecked == true)
            {
                return "random";
            }

            if (ModeCombo.IsChecked == true)
            {
                return "combo";
            }

            if (ModeManual.IsChecked == true)
            {
                return "manual";
            }

            return "single";
        }
    }

    private void SetMode(string mode)
    {
        _suppressUiEvents = true;
        ModeSingle.IsChecked = mode == "single";
        ModeBatch.IsChecked = mode == "batch";
        ModeRandom.IsChecked = mode == "random";
        ModeCombo.IsChecked = mode == "combo";
        ModeManual.IsChecked = mode == "manual";
        _suppressUiEvents = false;

        if (mode == "manual")
        {
            _manualCollage.Layout = SelectedLayout;
            _manualCollage.Borderless = BorderlessCheck.IsChecked == true;
            EnsureManualCollageForLayout(_manualCollage.Layout);
        }

        UpdateModeUi();
        SchedulePreviewRefresh();
    }

    private int SelectedCount
    {
        get => int.TryParse(CountBox.Text, out int count) ? Math.Max(1, count) : 1;
        set => CountBox.Text = value.ToString();
    }

    private string SelectedColor
    {
        get
        {
            if (ColorCombo.SelectedItem is ComboBoxItem item && item.Tag is string value &&
                !string.Equals(value, "__custom__", StringComparison.Ordinal))
            {
                return value;
            }

            return _customColor ?? "white";
        }
    }

    private string? _customColor;

    private void LoadSettingsIntoUi()
    {
        AppSettings settings = AppSettingsStore.Load();
        _suppressUiEvents = true;

        InputFolderBox.Text = settings.InputFolder;
        OutputFolderBox.Text = settings.OutputFolder;

        if (UiConstants.LayoutOrder.Contains(settings.Layout, StringComparer.OrdinalIgnoreCase))
        {
            HighlightLayoutCard(settings.Layout);
        }

        ModeSingle.IsChecked = settings.Mode == "single";
        ModeBatch.IsChecked = settings.Mode == "batch";
        ModeRandom.IsChecked = settings.Mode == "random";
        ModeCombo.IsChecked = settings.Mode == "combo";
        ModeManual.IsChecked = settings.Mode == "manual";
        SelectedCount = settings.Count;
        BorderlessCheck.IsChecked = settings.Borderless;
        BleedCheck.IsChecked = settings.Bleed;
        SelectColorInCombo(settings.Color);

        _manualCollage = EditableCollage.Blank(settings.Layout, settings.Borderless);

        _suppressUiEvents = false;
        UpdateModeUi();
    }

    private void SelectColorInCombo(string color)
    {
        foreach (object item in ColorCombo.Items)
        {
            if (item is ComboBoxItem comboItem &&
                comboItem.Tag is string value &&
                string.Equals(value, color, StringComparison.OrdinalIgnoreCase))
            {
                ColorCombo.SelectedItem = comboItem;
                _customColor = null;
                return;
            }
        }

        _customColor = color;
        ColorCombo.SelectedItem = null;
        ColorCombo.Text = color;
    }

    private AppSettings CollectSettings() => new()
    {
        InputFolder = InputFolderBox.Text.Trim(),
        OutputFolder = OutputFolderBox.Text.Trim(),
        Layout = SelectedLayout,
        Mode = SelectedMode,
        Count = SelectedCount,
        Borderless = BorderlessCheck.IsChecked == true,
        Bleed = BleedCheck.IsChecked == true,
        Color = SelectedColor,
    };

    private void SaveSettings() => AppSettingsStore.Save(CollectSettings());

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        _thumbnailLoader.Dispose();
        _previewStage.Dispose();
        _deckService.Dispose();
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        string? folder = PickFolder(InputFolderBox.Text);
        if (folder is not null)
        {
            InputFolderBox.Text = folder;
            ReloadThumbnails();
            UpdateRunEstimate();
            SchedulePreviewRefresh();
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        string? folder = PickFolder(OutputFolderBox.Text);
        if (folder is not null)
        {
            OutputFolderBox.Text = folder;
        }
    }

    private static string? PickFolder(string? initial)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select folder",
        };

        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
        {
            dialog.InitialDirectory = IOPath.GetFullPath(initial);
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void FolderBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressUiEvents || _busy)
        {
            return;
        }

        ReloadThumbnails();
        UpdateRunEstimate();
        SchedulePreviewRefresh();
    }

    private void ReloadThumbnails()
    {
        string folder = InputFolderBox.Text.Trim();
        _thumbnailLoader.LoadFolder(folder);
    }

    private void OnThumbnailsUpdated(IReadOnlyList<ThumbnailItem> items)
    {
        ThumbList.ItemsSource = null;
        ThumbList.ItemsSource = items;
    }

    private void RefreshPhotos_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        ReloadThumbnails();
    }

    private void LayoutCard_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (sender is Button { Tag: string layout })
        {
            HighlightLayoutCard(layout);
            if (SelectedMode == "manual")
            {
                _manualCollage.Layout = layout;
                _manualCollage.Borderless = BorderlessCheck.IsChecked == true;
                ResetManualCollageForLayout(layout);
            }

            UpdateRunEstimate();
            SchedulePreviewRefresh();
        }
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        // Checked fires while XAML is still wiring controls; wait until ctor finishes.
        if (!_uiReady || _suppressUiEvents || _busy)
        {
            return;
        }

        if (SelectedMode == "manual")
        {
            _manualCollage.Layout = SelectedLayout;
            _manualCollage.Borderless = BorderlessCheck.IsChecked == true;
            EnsureManualCollageForLayout(_manualCollage.Layout);
        }

        UpdateModeUi();
        UpdateRunEstimate();
        SchedulePreviewRefresh();
    }

    private void UpdateModeUi()
    {
        string mode = SelectedMode;
        bool combo = mode == "combo";
        bool manual = mode == "manual";
        bool enableEditing = !_busy;

        foreach (Button button in _layoutButtons.Values)
        {
            button.IsEnabled = enableEditing && !combo;
        }

        ModeSingle.IsEnabled = enableEditing;
        ModeBatch.IsEnabled = enableEditing;
        ModeRandom.IsEnabled = enableEditing;
        ModeCombo.IsEnabled = enableEditing;
        ModeManual.IsEnabled = enableEditing;

        InputFolderBox.IsReadOnly = !enableEditing;
        BrowseInputButton.IsEnabled = enableEditing;
        RefreshPhotosButton.IsEnabled = enableEditing;

        DeckList.IsEnabled = enableEditing;
        DeckSelectAllButton.IsEnabled = enableEditing;
        DeckSelectNoneButton.IsEnabled = enableEditing;

        BorderlessCheck.IsEnabled = enableEditing && !combo;
        BleedCheck.IsEnabled = enableEditing && !combo && BorderlessCheck.IsChecked != true;
        CountBox.IsEnabled = enableEditing && mode is "single" or "random";

        PreviewPrevButton.IsEnabled = enableEditing && !manual;
        PreviewNextButton.IsEnabled = enableEditing && !manual;

        if (manual)
        {
            int filled = _manualCollage.Slots.Count(s => s is not null);
            int required = _manualCollage.Slots.Count;
            if (filled > 0)
            {
                StatusText.Text =
                    $"Blank collage: {filled}/{required} slots filled. Drag/click thumbs; pan, flip, swap, clear; Ctrl+Z/Y undo.";
            }
            else
            {
                StatusText.Text =
                    "Blank collage: drag or click thumbnails to fill slots; pan, flip, swap, clear; Ctrl+Z/Y undo.";
            }
        }
        else if (GetFocusedCollage() is not null)
        {
            StatusText.Text = "Edit on stage: assign, pan, flip, swap, clear; Ctrl+Z/Y undo.";
        }
    }

    private void CountBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        UpdateRunEstimate();
        SchedulePreviewRefresh();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        if (SelectedMode == "manual")
        {
            _manualCollage.Borderless = BorderlessCheck.IsChecked == true;
        }

        UpdateRunEstimate();
        SchedulePreviewRefresh();
    }

    private void ColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        if (ColorCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            if (tag == "__custom__")
            {
                PromptCustomColor();
                return;
            }

            _customColor = null;
            ColorCombo.Text = string.Empty;
        }

        SchedulePreviewRefresh();
    }

    private void PromptCustomColor()
    {
        string current = _customColor ?? SelectedColor;
        var dialog = new CustomColorDialog(current) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ColorValue))
        {
            _customColor = dialog.ColorValue.Trim();
            ColorCombo.SelectedItem = null;
            ColorCombo.Text = _customColor;
            SchedulePreviewRefresh();
        }
        else if (_customColor is null)
        {
            SelectColorInCombo("white");
        }
        else
        {
            ColorCombo.Text = _customColor;
        }
    }

    private void UpdateRunEstimate()
    {
        string input = InputFolderBox.Text.Trim();
        string mode = SelectedMode;
        string layout = mode == "manual" ? _manualCollage.Layout : SelectedLayout;
        int count = SelectedCount;
        bool borderless = mode == "manual" ? _manualCollage.Borderless : BorderlessCheck.IsChecked == true;

        if (mode == "manual")
        {
            int required = _manualCollage.Slots.Count;
            int filled = _manualCollage.Slots.Count(s => s is not null);
            if (filled < required)
            {
                RunInfoText.Text = $"Blank collage: fill all {required} slots to export ({filled}/{required} filled).";
            }
            else
            {
                RunInfoText.Text = "Blank collage: Run writes one JPEG with current slot crops and transforms.";
            }

            return;
        }

        int expected = ExportService.EstimateOutputCount(input, mode, layout, count, borderless);
        if (expected == 0)
        {
            RunInfoText.Text = "No collages to generate with current folder and settings.";
            return;
        }

        if (DeckService.ShouldShowDeck(mode, expected))
        {
            int selected = _deckService.SelectedCount;
            string modeLabel = mode switch
            {
                "batch" => "batch",
                "random" => $"random × {count}",
                _ => "combo pack",
            };
            RunInfoText.Text =
                $"Deck: {expected} card(s) — {selected} ticked. Run asks export current or all ticked ({modeLabel}).";
            return;
        }

        string singleModeLabel = mode switch
        {
            "batch" => "batch",
            "random" => $"random × {count}",
            "combo" => "combo pack",
            _ => $"single (count {count})",
        };

        RunInfoText.Text = $"Will write {expected} file(s) — {singleModeLabel}, layout {layout}.";
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        new ShortcutsWindow { Owner = this }.ShowDialog();
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        string input = InputFolderBox.Text.Trim();
        string output = OutputFolderBox.Text.Trim();
        string mode = SelectedMode;
        string layout = SelectedMode == "manual" ? _manualCollage.Layout : SelectedLayout;
        int count = SelectedCount;
        bool borderless = SelectedMode == "manual" ? _manualCollage.Borderless : BorderlessCheck.IsChecked == true;
        bool bleed = BleedCheck.IsChecked == true;
        string color = SelectedColor;

        string? validationError = ExportService.ValidateRun(input, output, mode, layout, count, borderless);
        if (validationError is not null)
        {
            MessageBox.Show(validationError, "Cannot run", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        bool useDeck = mode != "manual"
            && DeckService.ShouldShowDeck(mode, ExportService.EstimateOutputCount(input, mode, layout, count, borderless));

        ExportChoice exportChoice = ExportChoice.Current;
        if (useDeck)
        {
            var choiceDialog = new ExportChoiceDialog(_deckService.SelectedCount) { Owner = this };
            if (choiceDialog.ShowDialog() != true)
            {
                return;
            }

            exportChoice = choiceDialog.Choice;

            if (exportChoice == ExportChoice.All && _deckService.SelectedCount == 0)
            {
                MessageBoxResult selectAll = MessageBox.Show(
                    "No cards are ticked. Select all cards and export?",
                    "Export all",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (selectAll != MessageBoxResult.Yes)
                {
                    return;
                }

                _deckService.SelectAll();
            }
        }

        string outputFull = IOPath.GetFullPath(output);
        IReadOnlyList<(ExportJob Job, EditableCollage Collage, string? ValidationContext)> exportTargets =
            BuildExportTargets(input, mode, layout, count, borderless, outputFull, exportChoice);

        foreach ((ExportJob _, EditableCollage collage, string? validationContext) in exportTargets)
        {
            string? collageError = ExportService.ValidateCollage(collage, validationContext);
            if (collageError is not null)
            {
                MessageBox.Show(collageError, "Cannot run", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        if (exportTargets.Count == 0)
        {
            MessageBox.Show("Nothing to export.", "Cannot run", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        IReadOnlyList<ExportJob> jobs = exportTargets.Select(t => t.Job).ToList();

        _busy = true;
        UpdateModeUi();
        RunButton.IsEnabled = false;
        OpenOutputButton.IsEnabled = false;
        PreviewPrevButton.IsEnabled = false;
        PreviewNextButton.IsEnabled = false;
        _lastRunOutputDir = null;

        RunProgress.Visibility = Visibility.Visible;
        RunProgress.Maximum = jobs.Count;
        RunProgress.Value = 0;
        StatusText.Text = "Working…";
        RunWorkingText.Text = "Processing…";

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Directory.CreateDirectory(outputFull);

            if (jobs.Count > 1)
            {
                int lo = Math.Max(2, (int)(jobs.Count * 0.2));
                int hi = Math.Max(lo + 4, (int)(jobs.Count * 0.9));
                RunWorkingText.Text =
                    $"Processing… about {lo}–{hi}s for ~{jobs.Count} file(s) (varies with CPU & photos).";
            }

            int completed = 0;
            ExportJobResult result = await Task.Run(() =>
                ExportJobRunner.RunJobs(
                    jobs,
                    outputFull,
                    color,
                    bleed,
                    onSuccess: _ =>
                    {
                        int done = Interlocked.Increment(ref completed);
                        Dispatcher.Invoke(() => RunProgress.Value = done);
                    },
                    onFailure: (index, message) =>
                        FileLogger.Warning($"Job {index:000} failed: {message}")),
                CancellationToken.None).ConfigureAwait(true);

            stopwatch.Stop();
            if (result.Failed > 0)
            {
                StatusText.Text =
                    $"Finished in {stopwatch.Elapsed.TotalSeconds:F1}s — {result.Succeeded}/{result.Total} succeeded.";
            }
            else
            {
                StatusText.Text =
                    $"Finished in {stopwatch.Elapsed.TotalSeconds:F1}s. Check output folder.";
            }

            if (result.Succeeded > 0)
            {
                _lastRunOutputDir = outputFull;
                OpenOutputButton.IsEnabled = true;
            }

            FileLogger.Info(
                $"Run complete: {result.Succeeded}/{result.Total} succeeded, {result.Failed} failed.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            FileLogger.Error("Run failed", ex);
            StatusText.Text = "Failed — see log.";
            MessageBox.Show(
                $"{ex.Message}\n\nSee log at {AppPaths.LogFile}",
                "Run failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            RunButton.IsEnabled = true;
            RunProgress.Visibility = Visibility.Collapsed;
            RunWorkingText.Text = string.Empty;
            UpdateModeUi();
            SchedulePreviewRefresh();
        }
    }

    private IReadOnlyList<(ExportJob Job, EditableCollage Collage, string? ValidationContext)> BuildExportTargets(
        string input,
        string mode,
        string layout,
        int count,
        bool borderless,
        string outputFull,
        ExportChoice exportChoice)
    {
        if (mode == "manual")
        {
            string outputPath = CollageExporter.GenerateManualOutputFilename(outputFull);
            return [(
                ExportService.BuildExportJobFromCollage(_manualCollage, jobIndex: 1, outputPath),
                _manualCollage,
                null)];
        }

        IReadOnlyList<ExportJob> allJobs = ExportService.BuildJobs(input, mode, layout, count, borderless);
        bool useDeck = DeckService.ShouldShowDeck(mode, allJobs.Count);

        if (useDeck)
        {
            if (exportChoice == ExportChoice.Current)
            {
                DeckCardItem card = _deckService.Cards[_deckService.FocusIndex];
                return [(
                    ExportService.BuildExportJobFromCollage(card.Collage, card.DeckIndex),
                    card.Collage,
                    $"Card {card.DeckIndex}")];
            }

            return _deckService.Cards
                .Where(c => c.IsSelected)
                .Select(c => (
                    ExportService.BuildExportJobFromCollage(c.Collage, c.DeckIndex),
                    c.Collage,
                    (string?)$"Card {c.DeckIndex}"))
                .ToList();
        }

        EditableCollage? collage = GetFocusedCollage();
        if (collage is null)
        {
            return Array.Empty<(ExportJob, EditableCollage, string?)>();
        }

        int focusIndex = Math.Clamp(_previewStage.FocusIndex, 0, Math.Max(0, allJobs.Count - 1));
        ExportJob template = allJobs[focusIndex];
        return [(
            ExportService.BuildExportJobFromCollage(collage, template.JobIndex),
            collage,
            null)];
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        string? folder = _lastRunOutputDir ?? OutputFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        try
        {
            string full = IOPath.GetFullPath(folder);
            Directory.CreateDirectory(full);
            Process.Start(new ProcessStartInfo
            {
                FileName = full,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            FileLogger.Warning($"Could not open output folder: {ex.Message}");
        }
    }

    private void SchedulePreviewRefresh(bool immediate = false)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        RebuildDeck();
        EnsureStandaloneCollages();
        int? deckFocus = DeckPanel.Visibility == Visibility.Visible ? _deckService.FocusIndex : null;
        _previewStage.ScheduleRefresh(BuildPreviewRequest(), immediate, deckFocus);
    }

    private void EnsureStandaloneCollages()
    {
        if (SelectedMode is "manual")
        {
            return;
        }

        if (DeckPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        string input = InputFolderBox.Text.Trim();
        string mode = SelectedMode;
        string layout = SelectedLayout;
        int count = SelectedCount;
        bool borderless = BorderlessCheck.IsChecked == true;

        IReadOnlyList<ExportJob> jobs = ExportService.BuildJobs(input, mode, layout, count, borderless);
        if (StandaloneCollagesMatch(jobs))
        {
            return;
        }

        _standaloneCollages = jobs.Select(EditableCollage.FromJob).ToList();
        _standaloneUndos = jobs.Select(_ => new ManualUndoStack()).ToList();
    }

    private bool StandaloneCollagesMatch(IReadOnlyList<ExportJob> jobs)
    {
        if (_standaloneCollages.Count != jobs.Count)
        {
            return false;
        }

        for (int i = 0; i < jobs.Count; i++)
        {
            if (!_standaloneCollages[i].MatchesJob(jobs[i]))
            {
                return false;
            }
        }

        return true;
    }

    private EditableCollage? GetFocusedCollage()
    {
        if (SelectedMode == "manual")
        {
            return _manualCollage;
        }

        if (DeckPanel.Visibility == Visibility.Visible && _deckService.Cards.Count > 0)
        {
            return _deckService.Cards[_deckService.FocusIndex].Collage;
        }

        if (_standaloneCollages.Count == 0)
        {
            return null;
        }

        int index = Math.Clamp(_previewStage.FocusIndex, 0, _standaloneCollages.Count - 1);
        return _standaloneCollages[index];
    }

    private ManualUndoStack GetFocusedUndo()
    {
        if (SelectedMode == "manual")
        {
            return _manualUndo;
        }

        if (DeckPanel.Visibility == Visibility.Visible && _deckService.Cards.Count > 0)
        {
            return _deckService.Cards[_deckService.FocusIndex].UndoStack;
        }

        int index = Math.Clamp(_previewStage.FocusIndex, 0, _standaloneUndos.Count - 1);
        return _standaloneUndos[index];
    }

    private bool HasEditableFocus() => GetFocusedCollage() is not null && !_busy;

    private void RebuildDeck()
    {
        string input = InputFolderBox.Text.Trim();
        string mode = SelectedMode;
        string layout = SelectedLayout;
        int count = SelectedCount;
        bool borderless = BorderlessCheck.IsChecked == true;
        string color = SelectedColor;
        bool bleed = BleedCheck.IsChecked == true;

        if (mode is "manual" or "single")
        {
            _deckService.Clear();
            DeckPanel.Visibility = Visibility.Collapsed;
            DeckList.ItemsSource = null;
            UpdateDeckSelectionText();
            return;
        }

        int jobCount = ExportService.EstimateOutputCount(input, mode, layout, count, borderless);
        if (!DeckService.ShouldShowDeck(mode, jobCount))
        {
            _deckService.Clear();
            DeckPanel.Visibility = Visibility.Collapsed;
            DeckList.ItemsSource = null;
            UpdateDeckSelectionText();
            return;
        }

        bool candidatesChanged = _deckService.RefreshOrRebuild(
            input, mode, layout, count, borderless, color, bleed);
        DeckPanel.Visibility = Visibility.Visible;

        if (candidatesChanged)
        {
            _syncingDeckFocus = true;
            DeckList.ItemsSource = _deckService.Cards;
            DeckList.SelectedIndex = _deckService.FocusIndex;
            _syncingDeckFocus = false;
        }

        UpdateDeckSelectionText();
        RequestVisibleDeckPreviews();
    }

    private void OnPreviewFocusIndexChanged(int index)
    {
        if (DeckPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        ClearStageInteractionState();
        SchedulePreviewRefresh(immediate: true);
    }

    private void OnDeckFocusIndexChanged(int index)
    {
        if (_syncingDeckFocus)
        {
            return;
        }

        _syncingDeckFocus = true;
        DeckList.SelectedIndex = index;
        _syncingDeckFocus = false;
        ClearStageInteractionState();
        SchedulePreviewRefresh(immediate: true);
        RequestVisibleDeckPreviews();
    }

    private void OnDeckSelectionChanged() => UpdateDeckSelectionText();

    private void UpdateDeckSelectionText()
    {
        if (DeckPanel.Visibility != Visibility.Visible)
        {
            DeckSelectionText.Text = string.Empty;
            return;
        }

        int total = _deckService.Cards.Count;
        int selected = _deckService.SelectedCount;
        DeckSelectionText.Text = $"{selected} of {total} ticked";
        UpdateRunEstimate();
    }

    private void RequestVisibleDeckPreviews()
    {
        if (DeckPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        var indices = new HashSet<int> { _deckService.FocusIndex };
        for (int i = 0; i < DeckList.Items.Count; i++)
        {
            if (DeckList.ItemContainerGenerator.ContainerFromIndex(i) is UIElement { IsVisible: true })
            {
                indices.Add(i);
            }
        }

        _deckService.RequestPreviewsForIndices(indices);
    }

    private void DeckSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _deckService.SelectAll();
    }

    private void DeckSelectNone_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _deckService.SelectNone();
    }

    private void DeckList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingDeckFocus || _busy || DeckList.SelectedIndex < 0)
        {
            return;
        }

        _deckService.SetFocusIndex(DeckList.SelectedIndex);
    }

    private void DeckList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source &&
            FindParent<CheckBox>(source) is not null)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject dep &&
            FindParent<ListBoxItem>(dep) is ListBoxItem item)
        {
            item.IsSelected = true;
            e.Handled = false;
        }
    }

    private void DeckCheck_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        e.Handled = false;

    private void DeckCardCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _deckService.NotifySelectionChanged();
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private PreviewRequest BuildPreviewRequest()
    {
        bool borderless = SelectedMode == "manual"
            ? _manualCollage.Borderless
            : BorderlessCheck.IsChecked == true;

        int focusIndex;
        int focusCount;
        if (SelectedMode == "manual")
        {
            focusIndex = 0;
            focusCount = 1;
        }
        else if (DeckPanel.Visibility == Visibility.Visible)
        {
            focusIndex = _deckService.FocusIndex;
            focusCount = _deckService.Cards.Count;
        }
        else
        {
            focusIndex = Math.Clamp(_previewStage.FocusIndex, 0, Math.Max(0, _standaloneCollages.Count - 1));
            focusCount = Math.Max(1, _standaloneCollages.Count);
        }

        return new PreviewRequest(
            InputFolderBox.Text.Trim(),
            SelectedMode,
            SelectedLayout,
            SelectedCount,
            borderless,
            BleedCheck.IsChecked == true,
            SelectedColor,
            GetFocusedCollage(),
            focusIndex,
            focusCount);
    }

    private void OnPreviewStageUpdated(PreviewStageState state)
    {
        PreviewNavText.Text = state.NavLabel;
        PreviewPrevButton.IsEnabled = state.CanPrev && !_busy;
        PreviewNextButton.IsEnabled = state.CanNext && !_busy;

        if (state.IsEmpty || state.Bitmap is null)
        {
            StageImage.Source = null;
            StageImage.Visibility = Visibility.Collapsed;
            StageOverlay.Children.Clear();
            StageEmptyText.Text = state.EmptyMessage;
            StageEmptyText.Visibility = Visibility.Visible;
            return;
        }

        StageImage.Source = state.Bitmap;
        StageImage.Visibility = Visibility.Visible;
        StageEmptyText.Visibility = Visibility.Collapsed;
        UpdateSwapOverlay();
    }

    private void PreviewPrev_Click(object sender, RoutedEventArgs e)
    {
        _previewStage.MovePrevious();
        SyncDeckToPreviewFocus();
    }

    private void PreviewNext_Click(object sender, RoutedEventArgs e)
    {
        _previewStage.MoveNext();
        SyncDeckToPreviewFocus();
    }

    private void SyncDeckToPreviewFocus()
    {
        if (DeckPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        int index = _previewStage.FocusIndex;
        _syncingDeckFocus = true;
        DeckList.SelectedIndex = index;
        _syncingDeckFocus = false;
        _deckService.SetFocusIndex(index);
    }

    private void PreviewArea_KeyDown(object sender, KeyEventArgs e) => HandlePreviewNavigation(e);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (HasEditableFocus() && Keyboard.Modifiers == ModifierKeys.Control && e.OriginalSource is not TextBox)
        {
            if (e.Key == Key.Z)
            {
                ManualUndo();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Y)
            {
                ManualRedo();
                e.Handled = true;
                return;
            }
        }

        HandlePreviewNavigation(e);
    }

    private void HandlePreviewNavigation(KeyEventArgs e)
    {
        if (_busy || e.OriginalSource is TextBox || SelectedMode == "manual")
        {
            return;
        }

        if (e.Key == Key.Left)
        {
            _previewStage.MovePrevious();
            SyncDeckToPreviewFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            _previewStage.MoveNext();
            SyncDeckToPreviewFocus();
            e.Handled = true;
        }
    }

    private void EnsureManualCollageForLayout(string layout)
    {
        int required = LayoutCatalog.GetRequired(layout).NumImages;
        if (_manualCollage.Layout == layout && _manualCollage.Slots.Count == required)
        {
            return;
        }

        ResetManualCollageForLayout(layout);
    }

    private void ResetManualCollageForLayout(string layout)
    {
        bool borderless = _manualCollage.Borderless;
        _manualCollage = EditableCollage.Blank(layout, borderless);
        _manualUndo.Clear();
        ClearStageInteractionState();
        UpdateRunEstimate();
        UpdateModeUi();
    }

    private void ClearStageInteractionState()
    {
        _stageDownSlot = null;
        _stagePanMoved = false;
        _panDragSlot = null;
        _panAnchor = null;
        _panLive = null;
        _swapPickupSlot = null;
        _swapHoverSlot = null;
        _previewStage.ClearManualLivePan();
        StageOverlay.Children.Clear();
    }

    private LayoutGeometry GetStageGeometry()
    {
        EditableCollage? collage = GetFocusedCollage();
        if (collage is null)
        {
            return LayoutGeometryCalculator.Compute("stack-3", false, BleedCheck.IsChecked == true);
        }

        return LayoutGeometryCalculator.Compute(
            collage.Layout,
            collage.Borderless,
            BleedCheck.IsChecked == true);
    }

    private ManualStageGeometry.StageMetrics GetStageMetrics() =>
        ManualStageGeometry.ComputeMetrics(StageBorder.ActualWidth, StageBorder.ActualHeight);

    private int? HitTestStageSlot(Point position)
    {
        ManualStageGeometry.StageMetrics metrics = GetStageMetrics();
        LayoutGeometry geometry = GetStageGeometry();
        return ManualStageGeometry.HitTestSlot(position.X, position.Y, metrics, geometry);
    }

    private void AssignToSlot(int slot, string path)
    {
        EditableCollage? collage = GetFocusedCollage();
        if (collage is null || slot < 0 || slot >= collage.Slots.Count)
        {
            return;
        }

        GetFocusedUndo().Checkpoint(collage.Slots);
        double panX = 0.0;
        double panY = 0.0;
        if (collage.Layout.Equals("grid-1x2-v", StringComparison.OrdinalIgnoreCase) && collage.Slots.Count == 2)
        {
            panX = slot == 0 ? -1.0 : 1.0;
        }

        collage.Slots[slot] = new SlotAssignment(path, panX, panY);
        CommitCollageEdit($"Assigned slot {slot + 1}.");
    }

    private void AssignThumbClick(string path)
    {
        EditableCollage? collage = GetFocusedCollage();
        if (collage is null)
        {
            return;
        }

        for (int i = 0; i < collage.Slots.Count; i++)
        {
            if (collage.Slots[i] is null)
            {
                AssignToSlot(i, path);
                return;
            }
        }

        StatusText.Text = "All slots are full — right-click a slot on the preview to clear one.";
    }

    private void ClearSlot(int slot)
    {
        EditableCollage? collage = GetFocusedCollage();
        if (collage is null || slot < 0 || slot >= collage.Slots.Count || collage.Slots[slot] is null)
        {
            return;
        }

        GetFocusedUndo().Checkpoint(collage.Slots);
        collage.Slots[slot] = null;
        CommitCollageEdit($"Cleared slot {slot + 1}.");
    }

    private void SwapSlots(int a, int b)
    {
        EditableCollage? collage = GetFocusedCollage();
        if (collage is null || a < 0 || b < 0 || a >= collage.Slots.Count || b >= collage.Slots.Count || a == b)
        {
            return;
        }

        if (collage.Slots[a] is null || collage.Slots[b] is null)
        {
            return;
        }

        GetFocusedUndo().Checkpoint(collage.Slots);
        (collage.Slots[a], collage.Slots[b]) = (collage.Slots[b], collage.Slots[a]);
        CommitCollageEdit($"Swapped slots {a + 1} and {b + 1}.");
    }

    private void ManualUndo()
    {
        EditableCollage? collage = GetFocusedCollage();
        if (collage is null)
        {
            return;
        }

        List<SlotAssignment?>? restored = GetFocusedUndo().Undo(collage.Slots);
        if (restored is null)
        {
            return;
        }

        collage.Slots.Clear();
        collage.Slots.AddRange(restored);
        ClearStageInteractionState();
        CommitCollageEdit("Undo.", refreshImmediate: true);
    }

    private void ManualRedo()
    {
        EditableCollage? collage = GetFocusedCollage();
        if (collage is null)
        {
            return;
        }

        List<SlotAssignment?>? restored = GetFocusedUndo().Redo(collage.Slots);
        if (restored is null)
        {
            return;
        }

        collage.Slots.Clear();
        collage.Slots.AddRange(restored);
        ClearStageInteractionState();
        CommitCollageEdit("Redo.", refreshImmediate: true);
    }

    private void CommitCollageEdit(string status, bool refreshImmediate = false)
    {
        StatusText.Text = status;
        UpdateRunEstimate();
        UpdateModeUi();
        if (DeckPanel.Visibility == Visibility.Visible &&
            _deckService.FocusIndex >= 0 &&
            _deckService.FocusIndex < _deckService.Cards.Count)
        {
            _deckService.InvalidatePreviewForIndex(_deckService.FocusIndex);
        }

        SchedulePreviewRefresh(refreshImmediate);
    }

    private void Thumb_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!HasEditableFocus() || sender is not FrameworkElement element ||
            element.DataContext is not ThumbnailItem item)
        {
            return;
        }

        _thumbDragPath = item.Path;
        _thumbDragStart = e.GetPosition(null);
        _thumbDragMoved = false;
        element.CaptureMouse();
    }

    private void Thumb_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_thumbDragPath is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point pos = e.GetPosition(null);
        double dx = pos.X - _thumbDragStart.X;
        double dy = pos.Y - _thumbDragStart.Y;
        if (dx * dx + dy * dy >= 16)
        {
            _thumbDragMoved = true;
        }
    }

    private void Thumb_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.ReleaseMouseCapture();
        }

        if (!HasEditableFocus() || _thumbDragPath is null)
        {
            _thumbDragPath = null;
            return;
        }

        string path = _thumbDragPath;
        _thumbDragPath = null;

        Point stagePoint = e.GetPosition(StageBorder);
        if (stagePoint.X >= 0 && stagePoint.Y >= 0 &&
            stagePoint.X <= StageBorder.ActualWidth && stagePoint.Y <= StageBorder.ActualHeight)
        {
            int? slot = HitTestStageSlot(stagePoint);
            if (slot is not null)
            {
                AssignToSlot(slot.Value, path);
                e.Handled = true;
                return;
            }
        }

        if (!_thumbDragMoved)
        {
            AssignThumbClick(path);
        }

        e.Handled = true;
    }

    private void Stage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!HasEditableFocus())
        {
            return;
        }

        EditableCollage? collage = GetFocusedCollage();
        if (collage is null)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            HandleStageDoubleClick(e);
            return;
        }

        Point pos = e.GetPosition(StageBorder);
        int? slot = HitTestStageSlot(pos);
        _swapHoverSlot = null;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && slot is not null && collage.Slots[slot.Value] is not null)
        {
            _swapPickupSlot = slot;
            _stageDownSlot = null;
            StageBorder.CaptureMouse();
            StatusText.Text =
                $"Slot {slot.Value + 1} picked — release on another slot to swap (crops stay with each photo).";
            UpdateSwapOverlay();
            e.Handled = true;
            return;
        }

        _swapPickupSlot = null;
        _stageDownSlot = slot;
        _stagePressPoint = pos;
        _stagePanMoved = false;
        _panDragSlot = null;
        _panAnchor = null;
        _panLive = null;
        StageBorder.CaptureMouse();
        e.Handled = true;
    }

    private void Stage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!HasEditableFocus())
        {
            return;
        }

        EditableCollage? collage = GetFocusedCollage();
        if (collage is null)
        {
            return;
        }

        Point pos = e.GetPosition(StageBorder);

        if (_swapPickupSlot is not null)
        {
            _swapHoverSlot = HitTestStageSlot(pos);
            UpdateSwapOverlay();
            return;
        }

        if (_stageDownSlot is null || collage.Slots[_stageDownSlot.Value] is null)
        {
            return;
        }

        int slot = _stageDownSlot.Value;
        double tcx = pos.X - _stagePressPoint.X;
        double tcy = pos.Y - _stagePressPoint.Y;
        if (!_stagePanMoved)
        {
            if (tcx * tcx + tcy * tcy < PanDragThresholdSq)
            {
                return;
            }

            _stagePanMoved = true;
            _panDragSlot = slot;
            SlotAssignment fill = collage.Slots[slot]!;
            _panAnchor = (fill.PanX, fill.PanY);
        }

        (double SensX, double SensY)? sens = ManualStageGeometry.PanSensitivity(
            slot,
            GetStageMetrics(),
            GetStageGeometry());
        if (sens is null || _panAnchor is null)
        {
            return;
        }

        (double ax, double ay) = _panAnchor.Value;
        _panLive = (
            Math.Clamp(ax - tcx * sens.Value.SensX, -1.0, 1.0),
            Math.Clamp(ay - tcy * sens.Value.SensY, -1.0, 1.0));
        _previewStage.SetManualLivePan(slot, _panLive.Value.X, _panLive.Value.Y);
    }

    private void Stage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!HasEditableFocus())
        {
            ClearStageInteractionState();
            StageBorder.ReleaseMouseCapture();
            return;
        }

        EditableCollage? collage = GetFocusedCollage();
        Point pos = e.GetPosition(StageBorder);

        if (_swapPickupSlot is not null)
        {
            int src = _swapPickupSlot.Value;
            int? tgt = HitTestStageSlot(pos);
            _swapPickupSlot = null;
            _swapHoverSlot = null;
            UpdateSwapOverlay();

            if (tgt is not null && tgt.Value != src)
            {
                SwapSlots(src, tgt.Value);
            }
            else
            {
                StatusText.Text = "Swap cancelled.";
            }

            StageBorder.ReleaseMouseCapture();
            _stageDownSlot = null;
            _stagePanMoved = false;
            return;
        }

        if (_stagePanMoved && _panDragSlot is not null && _panLive is not null && collage is not null)
        {
            int slot = _panDragSlot.Value;
            SlotAssignment? fill = collage.Slots[slot];
            if (fill is not null)
            {
                _previewStage.FlushPendingRender();
                _previewStage.ClearManualLivePan();
                GetFocusedUndo().Checkpoint(collage.Slots);
                collage.Slots[slot] = fill with { PanX = _panLive.Value.X, PanY = _panLive.Value.Y };
                CommitCollageEdit($"Slot {slot + 1} pan updated.", refreshImmediate: true);
            }
        }
        else
        {
            _previewStage.FlushPendingRender();
            _previewStage.ClearManualLivePan();
            SchedulePreviewRefresh(immediate: true);
        }

        _panDragSlot = null;
        _panAnchor = null;
        _panLive = null;
        _stageDownSlot = null;
        _stagePanMoved = false;
        StageBorder.ReleaseMouseCapture();
    }

    private void HandleStageDoubleClick(MouseButtonEventArgs e)
    {
        EditableCollage? collage = GetFocusedCollage();
        ClearStageInteractionState();
        Point pos = e.GetPosition(StageBorder);
        int? slot = HitTestStageSlot(pos);
        if (collage is null || slot is null || collage.Slots[slot.Value] is not SlotAssignment fill)
        {
            return;
        }

        GetFocusedUndo().Checkpoint(collage.Slots);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            collage.Slots[slot.Value] = fill with { Grayscale = !fill.Grayscale };
            CommitCollageEdit(
                $"Slot {slot.Value + 1}: black & white {(!fill.Grayscale ? "on" : "off")}.",
                refreshImmediate: true);
        }
        else
        {
            collage.Slots[slot.Value] = fill with { FlipH = !fill.FlipH };
            CommitCollageEdit(
                $"Slot {slot.Value + 1}: horizontal flip {(!fill.FlipH ? "on" : "off")}.",
                refreshImmediate: true);
        }

        e.Handled = true;
    }

    private void Stage_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!HasEditableFocus())
        {
            return;
        }

        Point pos = e.GetPosition(StageBorder);
        int? slot = HitTestStageSlot(pos);
        if (slot is not null)
        {
            ClearSlot(slot.Value);
            e.Handled = true;
        }
    }

    private void UpdateSwapOverlay()
    {
        StageOverlay.Children.Clear();
        if (_swapPickupSlot is null || !HasEditableFocus())
        {
            return;
        }

        ManualStageGeometry.StageMetrics metrics = GetStageMetrics();
        LayoutGeometry geometry = GetStageGeometry();
        IReadOnlyList<(int X0, int Y0, int X1, int Y1)> rects =
            ManualStageGeometry.SlotPixelRects(metrics, geometry);

        StageOverlay.Width = metrics.DisplayWidth;
        StageOverlay.Height = metrics.DisplayHeight;

        void AddHighlight(int index, Brush stroke, double thickness)
        {
            if (index < 0 || index >= rects.Count)
            {
                return;
            }

            (int x0, int y0, int x1, int y1) = rects[index];
            var rect = new Rectangle
            {
                Width = Math.Max(1, x1 - x0),
                Height = Math.Max(1, y1 - y0),
                Stroke = stroke,
                StrokeThickness = thickness,
                Fill = Brushes.Transparent,
            };
            Canvas.SetLeft(rect, x0 - metrics.OffsetX);
            Canvas.SetTop(rect, y0 - metrics.OffsetY);
            StageOverlay.Children.Add(rect);
        }

        AddHighlight(_swapPickupSlot.Value, new SolidColorBrush(Color.FromRgb(0x66, 0xCC, 0xFF)), 2);
        if (_swapHoverSlot is int hover && hover != _swapPickupSlot)
        {
            AddHighlight(hover, new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x33)), 3);
        }
    }
}
