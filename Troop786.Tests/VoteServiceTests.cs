using Troop786.Core;
using Xunit;

namespace Troop786.Tests;

public class VoteServiceTests
{
    private static (KioskVoteService svc, InMemoryKioskStore store) Setup(params RaceState[] races)
    {
        var store = new InMemoryKioskStore();
        foreach (var r in races) store.AddRace(r);
        return (new KioskVoteService(store), store);
    }

    private static Task<VoteBatchResponse> Submit(KioskVoteService svc, params VoteSubmission[] votes) =>
        svc.SubmitAsync(new VoteBatchRequest(votes));

    [Fact]
    public async Task ValidVote_IsAccepted()
    {
        var (svc, store) = Setup(Make.Open());
        var resp = await Submit(svc, Make.Vote("s1", "A"));
        Assert.Equal(VoteOutcome.Accepted, resp.Results[0].Outcome);
        Assert.Single(store.Votes);
    }

    [Fact]
    public async Task ReplayOfSameVoteId_IsAcceptedWithoutDuplicateOrFlag()
    {
        var (svc, store) = Setup(Make.Open());
        var vote = Make.Vote("s1", "A");
        await Submit(svc, vote);
        var again = await Submit(svc, vote);
        Assert.Equal(VoteOutcome.Accepted, again.Results[0].Outcome);
        Assert.Single(store.Votes);
        Assert.Empty(store.Flags);
    }

    [Fact]
    public async Task SecondVoteFromSameScout_FirstWins_LaterIsFlaggedNotCounted()
    {
        var (svc, store) = Setup(Make.Open());
        var first = Make.Vote("s1", "A");
        var second = Make.Vote("s1", "B");
        await Submit(svc, first);
        var resp = await Submit(svc, second);

        Assert.Equal(VoteOutcome.Duplicate, resp.Results[0].Outcome);
        Assert.Single(store.Votes);
        Assert.Equal("A", store.Votes.Values.Single().Vote.CandidateId);
        var flag = Assert.Single(store.Flags);
        Assert.Equal(first.VoteId, flag.ExistingVoteId);
    }

    [Fact]
    public async Task UnknownRace_IsRejected()
    {
        var (svc, _) = Setup();
        var resp = await Submit(svc, Make.Vote("s1", "A"));
        Assert.Equal(VoteOutcome.Rejected, resp.Results[0].Outcome);
    }

    [Fact]
    public async Task ClosedRace_RejectsVoteCastAfterClose()
    {
        var closedAt = Make.T0;
        var (svc, store) = Setup(Make.Closed(closedAt));
        var resp = await Submit(svc, Make.Vote("s1", "A", castAt: closedAt.AddMinutes(1)));
        Assert.Equal(VoteOutcome.Rejected, resp.Results[0].Outcome);
        Assert.Empty(store.Votes);
    }

    [Fact]
    public async Task ClosedRace_AcceptsLateUploadOfVoteCastBeforeClose()
    {
        var closedAt = Make.T0;
        var (svc, store) = Setup(Make.Closed(closedAt));
        var resp = await Submit(svc, Make.Vote("s1", "A", castAt: closedAt.AddMinutes(-5)));
        Assert.Equal(VoteOutcome.Accepted, resp.Results[0].Outcome);
        Assert.Single(store.Votes);
    }

    [Fact]
    public async Task Runoff_IsSeparateRecordFromFirstRound()
    {
        var (svc, store) = Setup(Make.Open(round: 1), Make.Open(round: 2));
        await Submit(svc, Make.Vote("s1", "A", round: 1));
        var resp = await Submit(svc, Make.Vote("s1", "B", round: 2));
        Assert.Equal(VoteOutcome.Accepted, resp.Results[0].Outcome);
        Assert.Equal(2, store.Votes.Count);
    }

    [Fact]
    public async Task Revert_DeletesVote_AuditsIt_AndReopensTheCode()
    {
        var (svc, store) = Setup(Make.Open());
        await Submit(svc, Make.Vote("s1", "A"));

        var reverted = await svc.RevertAsync(Make.Cycle, "SPL", 1, "s1", "admin");
        Assert.True(reverted);
        Assert.Empty(store.Votes);
        Assert.Equal("RevertVote", Assert.Single(store.Audit).Action);

        var again = await Submit(svc, Make.Vote("s1", "B"));
        Assert.Equal(VoteOutcome.Accepted, again.Results[0].Outcome);
    }

    [Fact]
    public async Task Revert_OfMissingVote_ReturnsFalseAndDoesNotAudit()
    {
        var (svc, store) = Setup(Make.Open());
        Assert.False(await svc.RevertAsync(Make.Cycle, "SPL", 1, "nobody", "admin"));
        Assert.Empty(store.Audit);
    }

    [Fact]
    public async Task InvalidVote_IsRejected()
    {
        var (svc, _) = Setup(Make.Open());
        var bad = Make.Vote("s1", "A", round: 0);
        var resp = await Submit(svc, bad);
        Assert.Equal(VoteOutcome.Rejected, resp.Results[0].Outcome);
    }
}
