using ImageStacker.Core;
using ImageStacker.Core.Layout;
using ImageStacker.Core.Contracts;
using ImageStacker.Core.Io;
using ImageStacker.Core.Jobs;

namespace ImageStacker.App.Services;

internal static class ExportService
{
  public static IReadOnlyList<ExportJob> BuildJobs(
    string inputFolder,
    string mode,
    string layout,
    int count,
    bool borderless)
  {
    return mode switch
    {
      "combo" => BuildComboJobs(inputFolder),
      "batch" => BuildLayoutJobs(inputFolder, layout, 1, batch: true, random: false, borderless),
      "random" => BuildLayoutJobs(inputFolder, layout, count, batch: false, random: true, borderless),
      _ => BuildLayoutJobs(inputFolder, layout, count, batch: false, random: false, borderless),
    };
  }

  public static int EstimateOutputCount(
    string inputFolder,
    string mode,
    string layout,
    int count,
    bool borderless)
  {
    if (mode == "manual")
    {
      return 1;
    }

    return BuildJobs(inputFolder, mode, layout, count, borderless).Count;
  }

  private static List<ExportJob> BuildComboJobs(string inputFolder)
  {
    IReadOnlyList<(IReadOnlyList<string> Paths, string LayoutName, bool Borderless)> sequences =
      LayoutContracts.ListComboSequences(inputFolder);

    return sequences
      .Select((seq, index) => new ExportJob(seq.Paths, seq.LayoutName, seq.Borderless, index + 1))
      .ToList();
  }

  private static List<ExportJob> BuildLayoutJobs(
    string inputFolder,
    string layout,
    int count,
    bool batch,
    bool random,
    bool borderless)
  {
    IReadOnlyList<IReadOnlyList<string>> candidates = LayoutContracts.ListLayoutCandidates(
      inputFolder,
      layout,
      count,
      batch,
      random,
      borderless);

    return candidates
      .Select((paths, index) => new ExportJob(paths, layout, borderless, index + 1))
      .ToList();
  }

  public static string? ValidateRun(
    string inputFolder,
    string outputFolder,
    string mode,
    string layout,
    int count,
    bool borderless,
    IReadOnlyList<SlotAssignment?>? manualSlots = null,
    string? manualLayout = null)
  {
    if (mode != "manual" && !Directory.Exists(inputFolder))
    {
      return "Input folder does not exist.";
    }

    try
    {
      Directory.CreateDirectory(outputFolder);
    }
    catch (Exception ex)
    {
      return $"Cannot create output folder: {ex.Message}";
    }

    if (mode == "manual")
    {
      if (manualSlots is null || manualLayout is null)
      {
        return "Manual slots are not initialized.";
      }

      int required = LayoutCatalog.GetRequired(manualLayout).NumImages;
      if (manualSlots.Count != required)
      {
        return $"Manual slots must match layout ({required} slots).";
      }

      if (manualSlots.Any(s => s is null))
      {
        return $"Fill all {required} slots on the preview.";
      }

      foreach (SlotAssignment? slot in manualSlots)
      {
        if (slot is null || !File.Exists(slot.Path))
        {
          return slot is null
            ? $"Fill all {required} slots on the preview."
            : $"Missing photo: {slot.Path}";
        }

        string? orientationError = OrientationHelper.GetOrientationMismatchMessage(
          slot.Path,
          LayoutCatalog.GetRequired(manualLayout).Orientation);
        if (orientationError is not null)
        {
          return orientationError;
        }
      }

      return null;
    }

    if (mode != "combo")
    {
      if (!LayoutCatalog.Layouts.ContainsKey(layout))
      {
        return $"Unknown layout '{layout}'.";
      }

      if (EstimateOutputCount(inputFolder, mode, layout, count, borderless) == 0)
      {
        return "Nothing to generate — check photos and mode (not enough matching images?).";
      }
    }
    else if (BuildComboJobs(inputFolder).Count == 0)
    {
      return "Nothing to generate — check photos and orientations for combo mode.";
    }

    return null;
  }
}
