using Troop786.Core;
using Xunit;

namespace Troop786.Tests;

public class SyncTests
{
    private static (OutboxSyncService sync, InMemoryOutbox outbox, FakeApi api, InMemoryKioskStore store) Setup()
    {
        var store = new InMemoryKioskStore();
        store.AddRace(Make.Open());
        var api = new FakeApi(new KioskVoteService(store));
        var outbox = new InMemoryOutbox();
        return (new OutboxSyncService(outbox, api), outbox, api, store);
    }

    [Fact]
    public async Task Offline_KeepsAllVotesQueued()
    {
        var (sync, outbox, api, store) = Setup();
        api.Offline = true;
        outbox.Pending.Add(Make.Vote("s1", "A"));
        outbox.Pending.Add(Make.Vote("s2", "B"));

        var summary = await sync.FlushAsync();

        Assert.True(summary.WentOffline);
        Assert.Equal(2, outbox.Pending.Count);
        Assert.Empty(store.Votes);
    }

    [Fact]
    public async Task WhenConnectionReturns_QueueDrainsAndVotesAreStored()
    {
        var (sync, outbox, api, store) = Setup();
        api.Offline = true;
        outbox.Pending.Add(Make.Vote("s1", "A"));
        await sync.FlushAsync();

        api.Offline = false;
        var summary = await sync.FlushAsync();

        Assert.False(summary.WentOffline);
        Assert.Equal(1, summary.Accepted);
        Assert.Empty(outbox.Pending);
        Assert.Single(store.Votes);
    }

    [Fact]
    public async Task DuplicateAcrossTablets_IsRemovedFromQueueAndFlaggedOnServer()
    {
        var (sync, outbox, _, store) = Setup();
        outbox.Pending.Add(Make.Vote("s1", "A"));
        outbox.Pending.Add(Make.Vote("s1", "B")); // same scout on a second tablet

        var summary = await sync.FlushAsync();

        Assert.Equal(1, summary.Accepted);
        Assert.Equal(1, summary.Duplicates);
        Assert.Empty(outbox.Pending);
        Assert.Single(store.Flags);
    }

    [Fact]
    public async Task RejectedVote_StaysLocallyMarked_AndIsNotRetried()
    {
        var (sync, outbox, api, _) = Setup();
        outbox.Pending.Add(Make.Vote("s1", "A", race: "NOPE")); // race does not exist

        var first = await sync.FlushAsync();
        var callsAfterFirst = api.Calls;
        var second = await sync.FlushAsync();

        Assert.Equal(1, first.Rejected);
        Assert.Single(outbox.Pending);
        Assert.Single(outbox.Rejected);
        Assert.Equal(0, second.Rejected);
        Assert.Equal(callsAfterFirst, api.Calls); // nothing left to post
    }

    [Fact]
    public async Task LargeQueue_IsUploadedInMultipleBatches()
    {
        var (sync, outbox, api, store) = Setup();
        for (var i = 0; i < 60; i++) outbox.Pending.Add(Make.Vote($"s{i}", "A"));

        var summary = await sync.FlushAsync();

        Assert.Equal(60, summary.Accepted);
        Assert.Equal(3, api.Calls); // 25 + 25 + 10
        Assert.Equal(60, store.Votes.Count);
    }
}
