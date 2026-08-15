namespace ImageStacker.Core.Jobs;

public sealed record ComboJobSpec(string LayoutName, int Count, bool Batch, bool Random, bool Borderless);

public static class ComboJobSpecs
{
    public static readonly IReadOnlyList<ComboJobSpec> All =
    [
        new("grid-2x4", 10, Batch: false, Random: true, Borderless: true),
        new("stack-3", 10, Batch: false, Random: true, Borderless: true),
        new("grid-2x2-v", 10, Batch: false, Random: true, Borderless: false),
        new("grid-2x2-v", 10, Batch: false, Random: true, Borderless: true),
    ];
}
