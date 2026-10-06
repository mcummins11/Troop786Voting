namespace Troop786.Core;

/// <summary>Local queue of votes waiting to upload. Implemented over SQLite on the tablet.</summary>
public interface IVoteOutbox
{
    /// <summary>Oldest pending votes first. Votes already marked rejected are excluded.</summary>
    Task<IReadOnlyList<VoteSubmission>> PeekAsync(int max, CancellationToken ct);

    Task RemoveAsync(IEnumerable<Guid> voteIds, CancellationToken ct);

    /// <summary>Keep the vote locally for admin attention, but stop retrying it.</summary>
    Task MarkRejectedAsync(Guid voteId, string reason, CancellationToken ct);

    Task<int> PendingCountAsync(CancellationToken ct);
}

public interface IKioskApi
{
    Task<VoteBatchResponse> PostVotesAsync(VoteBatchRequest request, CancellationToken ct);
}

public sealed record SyncSummary(int Accepted, int Duplicates, int Rejected, bool WentOffline);

public sealed class OutboxSyncService
{
    private const int BatchSize = 25;
    private readonly IVoteOutbox _outbox;
    private readonly IKioskApi _api;

    public OutboxSyncService(IVoteOutbox outbox, IKioskApi api)
    {
        _outbox = outbox;
        _api = api;
    }

    /// <summary>
    /// Uploads queued votes in batches until the queue is empty or the network fails.
    /// The server decision is final: accepted and duplicate votes leave the queue (duplicates are flagged
    /// server-side for admin review); rejected votes stay locally, marked, and are not retried.
    /// </summary>
    public async Task<SyncSummary> FlushAsync(CancellationToken ct = default)
    {
        int accepted = 0, duplicates = 0, rejected = 0;

        while (true)
        {
            var batch = await _outbox.PeekAsync(BatchSize, ct);
            if (batch.Count == 0)
                return new SyncSummary(accepted, duplicates, rejected, false);

            VoteBatchResponse response;
            try
            {
                response = await _api.PostVotesAsync(new VoteBatchRequest(batch), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                return new SyncSummary(accepted, duplicates, rejected, true);
            }

            var done = new List<Guid>();
            foreach (var r in response.Results)
            {
                switch (r.Outcome)
                {
                    case VoteOutcome.Accepted: accepted++; done.Add(r.VoteId); break;
                    case VoteOutcome.Duplicate: duplicates++; done.Add(r.VoteId); break;
                    case VoteOutcome.Rejected:
                        rejected++;
                        await _outbox.MarkRejectedAsync(r.VoteId, r.Reason ?? "Rejected", ct);
                        break;
                }
            }
            if (done.Count > 0)
                await _outbox.RemoveAsync(done, ct);

            // No result for any vote in the batch means no progress is possible; stop instead of looping.
            if (response.Results.Count == 0)
                return new SyncSummary(accepted, duplicates, rejected, false);
        }
    }
}
