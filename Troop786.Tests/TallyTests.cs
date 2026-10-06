using Troop786.Core;
using Xunit;

namespace Troop786.Tests;

public class TallyTests
{
    [Fact]
    public void MostVotesWins_NoMajorityNeeded()
    {
        // 4 of 10 is a plurality, not a majority. It still wins.
        var votes = new[] { "A", "A", "A", "A", "B", "B", "B", "C", "C", "D" };
        var r = RaceTally.Compute(votes);
        Assert.Equal("A", r.Winner);
        Assert.False(r.IsTie);
    }

    [Fact]
    public void ExactTieForFirst_TriggersRunoffOfTiedCandidatesOnly()
    {
        var r = RaceTally.Compute(new[] { "A", "A", "B", "B", "C" });
        Assert.True(r.IsTie);
        Assert.False(r.HasWinner);
        Assert.Equal(new[] { "A", "B" }, r.TiedCandidates);
    }

    [Fact]
    public void TieBelowFirstPlace_IsNotATie()
    {
        var r = RaceTally.Compute(new[] { "A", "A", "A", "B", "C" });
        Assert.Equal("A", r.Winner);
        Assert.False(r.IsTie);
    }

    [Fact]
    public void NoVotes_HasNoWinnerAndNoTie()
    {
        var r = RaceTally.Compute(Array.Empty<string>());
        Assert.False(r.HasWinner);
        Assert.False(r.IsTie);
    }

    [Fact]
    public void SingleCandidate_IsAutoElected()
    {
        Assert.True(RaceTally.IsAutoElected(new[] { "A" }));
        Assert.False(RaceTally.IsAutoElected(new[] { "A", "B" }));
    }
}
