namespace Troop786.Core;

public sealed record TallyResult(
    IReadOnlyDictionary<string, int> Counts,
    string? Winner,
    IReadOnlyList<string> TiedCandidates)
{
    /// <summary>True only when two or more candidates share the top count (exact tie = runoff).</summary>
    public bool IsTie => TiedCandidates.Count > 1;

    public bool HasWinner => Winner is not null;
}

public static class RaceTally
{
    /// <summary>
    /// Rule: the candidate with the most votes wins. No majority threshold.
    /// Only an exact tie for first place triggers a runoff between the tied candidates.
    /// An empty vote list has no winner and no tie.
    /// </summary>
    public static TallyResult Compute(IEnumerable<string> candidateIds)
    {
        var counts = candidateIds
            .GroupBy(c => c, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        if (counts.Count == 0)
            return new TallyResult(counts, null, Array.Empty<string>());

        var max = counts.Values.Max();
        var top = counts
            .Where(kv => kv.Value == max)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        return top.Count == 1
            ? new TallyResult(counts, top[0], top)
            : new TallyResult(counts, null, top);
    }

    /// <summary>A race with exactly one candidate is decided without voting.</summary>
    public static bool IsAutoElected(IReadOnlyCollection<string> candidateIds) => candidateIds.Count == 1;
}
