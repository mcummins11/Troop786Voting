namespace Troop786.Core;

/// <summary>Persistence the vote service needs. Implemented over DynamoDB in Troop786.Aws.</summary>
public interface IKioskStore
{
    Task<RaceState?> GetRaceAsync(string cycleId, string raceId, int round, CancellationToken ct);

    /// <summary>Conditional put: returns false when a vote already exists for this scout, race and round.</summary>
    Task<bool> TryPutVoteAsync(VoteRecord record, CancellationToken ct);

    Task<VoteRecord?> GetVoteAsync(string cycleId, string raceId, int round, string scoutId, CancellationToken ct);

    /// <summary>Returns true when a vote was actually deleted.</summary>
    Task<bool> DeleteVoteAsync(string cycleId, string raceId, int round, string scoutId, CancellationToken ct);

    Task PutFlagAsync(VoteFlag flag, CancellationToken ct);

    Task AppendAuditAsync(string cycleId, AuditEntry entry, CancellationToken ct);
}

public sealed class KioskVoteService
{
    private readonly IKioskStore _store;
    private readonly TimeProvider _clock;

    public KioskVoteService(IKioskStore store, TimeProvider? clock = null)
    {
        _store = store;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<VoteBatchResponse> SubmitAsync(VoteBatchRequest request, CancellationToken ct = default)
    {
        var results = new List<VoteResult>(request.Votes.Count);
        foreach (var vote in request.Votes)
            results.Add(await SubmitOneAsync(vote, ct));
        return new VoteBatchResponse(results);
    }

    private async Task<VoteResult> SubmitOneAsync(VoteSubmission v, CancellationToken ct)
    {
        if (v.VoteId == Guid.Empty || string.IsNullOrWhiteSpace(v.CycleId) || string.IsNullOrWhiteSpace(v.ScoutId)
            || string.IsNullOrWhiteSpace(v.RaceId) || string.IsNullOrWhiteSpace(v.CandidateId) || v.Round < 1)
            return new VoteResult(v.VoteId, VoteOutcome.Rejected, "Invalid vote");

        var race = await _store.GetRaceAsync(v.CycleId, v.RaceId, v.Round, ct);
        if (race is null)
            return new VoteResult(v.VoteId, VoteOutcome.Rejected, "Unknown race");

        // A vote cast while the round was open is still valid when it uploads after the round closed.
        if (race.Status != RaceStatus.Open && (race.ClosedAt is null || v.CastAt > race.ClosedAt))
            return new VoteResult(v.VoteId, VoteOutcome.Rejected, "Voting closed");

        var record = new VoteRecord(v, _clock.GetUtcNow());

        // Two attempts covers the narrow window where an admin reverts the existing vote mid-check.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (await _store.TryPutVoteAsync(record, ct))
                return new VoteResult(v.VoteId, VoteOutcome.Accepted);

            var existing = await _store.GetVoteAsync(v.CycleId, v.RaceId, v.Round, v.ScoutId, ct);
            if (existing is null)
                continue; // reverted between the put and the get; try again

            // Replay of an upload the server already stored (tablet never saw the response).
            if (existing.Vote.VoteId == v.VoteId)
                return new VoteResult(v.VoteId, VoteOutcome.Accepted);

            // First accepted vote wins. The later one is kept for admin review but never counted.
            await _store.PutFlagAsync(
                new VoteFlag(v, existing.Vote.VoteId, "Scout already voted in this round", _clock.GetUtcNow()), ct);
            return new VoteResult(v.VoteId, VoteOutcome.Duplicate, "Scout already voted in this round");
        }

        return new VoteResult(v.VoteId, VoteOutcome.Rejected, "Could not record vote, retry");
    }

    /// <summary>
    /// Admin revert: deletes the scout's vote for that round, which reopens their code for a new ballot.
    /// Every revert is audited.
    /// </summary>
    public async Task<bool> RevertAsync(
        string cycleId, string raceId, int round, string scoutId, string actor, CancellationToken ct = default)
    {
        var deleted = await _store.DeleteVoteAsync(cycleId, raceId, round, scoutId, ct);
        if (deleted)
        {
            await _store.AppendAuditAsync(
                cycleId,
                new AuditEntry(_clock.GetUtcNow(), actor, "RevertVote", $"{raceId} round {round} scout {scoutId}"),
                ct);
        }
        return deleted;
    }
}
