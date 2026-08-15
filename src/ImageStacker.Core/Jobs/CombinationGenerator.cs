namespace ImageStacker.Core.Jobs;

public static class CombinationGenerator
{
    public static IReadOnlyList<IReadOnlyList<string>> Generate(
        IReadOnlyList<string> allPaths,
        int numImages,
        bool batch,
        bool randomMode,
        int count)
    {
        int totalFiles = allPaths.Count;
        if (totalFiles < numImages)
        {
            return Array.Empty<IReadOnlyList<string>>();
        }

        var combinations = new List<IReadOnlyList<string>>();

        if (batch)
        {
            var pool = allPaths.ToList();
            if (randomMode)
            {
                Shuffle(pool);
            }

            int numGrids = totalFiles / numImages;
            for (int i = 0; i < numGrids; i++)
            {
                combinations.Add(pool.Skip(i * numImages).Take(numImages).ToList());
            }
        }
        else if (randomMode)
        {
            var pool = allPaths.ToList();
            Shuffle(pool);

            for (int i = 0; i < count; i++)
            {
                if (pool.Count < numImages)
                {
                    pool = allPaths.ToList();
                    Shuffle(pool);
                }

                var combo = new List<string>(numImages);
                for (int j = 0; j < numImages; j++)
                {
                    int last = pool.Count - 1;
                    combo.Add(pool[last]);
                    pool.RemoveAt(last);
                }

                combinations.Add(combo);
            }
        }
        else
        {
            combinations.Add(allPaths.Take(numImages).ToList());
        }

        return combinations;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
