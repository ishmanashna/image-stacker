using ImageStacker.Core;
using ImageStacker.Core.Layout;
using ImageStacker.Core.Contracts;
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

  public static ExportJob BuildExportJobFromCollage(
    EditableCollage collage,
    int jobIndex,
    string? outputPath = null)
  {
    var paths = collage.Slots.Select(s => s!.Path).ToList();
    var slots = collage.Slots.Select(s => s!).ToList();
    return new ExportJob(paths, collage.Layout, collage.Borderless, jobIndex, slots, outputPath);
  }

  public static string? ValidateCollage(EditableCollage collage, string? context = null)
  {
    string prefix = context is null ? string.Empty : $"{context}: ";
    int required = LayoutCatalog.GetRequired(collage.Layout).NumImages;

    if (collage.Slots.Count != required)
    {
      return $"{prefix}Slots must match layout ({required} slots).";
    }

    if (collage.Slots.Any(s => s is null))
    {
      return $"{prefix}Fill all {required} slots on the preview.";
    }

    foreach (SlotAssignment? slot in collage.Slots)
    {
      if (slot is null || !File.Exists(slot.Path))
      {
        return slot is null
          ? $"{prefix}Fill all {required} slots on the preview."
          : $"{prefix}Missing photo: {slot.Path}";
      }
    }

    return null;
  }

  public static string? ValidateRun(
    string inputFolder,
    string outputFolder,
    string mode,
    string layout,
    int count,
    bool borderless)
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
