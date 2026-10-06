using Troop786.Core;

namespace Troop786.Tests;

public sealed class InMemoryKioskStore : IKioskStore
{
    public readonly Dictionary<string, RaceState> Races = new();
    public readonly Dictionary<string, VoteRecord> Votes = new();
    public readonly List<VoteFlag> Flags = new();
    public readonly List<AuditEntry> Audit = new();

    private static string RaceKey(string c, string r, int n) => $"{c}|{r}|{n}";
    private static string VoteKey(string c, string r, int n, string s) => $"{c}|{r}|{n}|{s}";

    public void AddRace(RaceState s) => Races[RaceKey(s.CycleId, s.RaceId, s.Round)] = s;

    public Task<RaceState?> GetRaceAsync(string cycleId, string raceId, int round, CancellationToken ct) =>
        Task.FromResult(Races.GetValueOrDefault(RaceKey(cycleId, raceId, round)));

    public Task<bool> TryPutVoteAsync(VoteRecord record, CancellationToken ct)
    {
        var v = record.Vote;
        return Task.FromResult(Votes.TryAdd(VoteKey(v.CycleId, v.RaceId, v.Round, v.ScoutId), record));
    }

    public Task<VoteRecord?> GetVoteAsync(string cycleId, string raceId, int round, string scoutId, CancellationToken ct) =>
        Task.FromResult(Votes.GetValueOrDefault(VoteKey(cycleId, raceId, round, scoutId)));

    public Task<bool> DeleteVoteAsync(string cycleId, string raceId, int round, string scoutId, CancellationToken ct) =>
        Task.FromResult(Votes.Remove(VoteKey(cycleId, raceId, round, scoutId)));

    public Task PutFlagAsync(VoteFlag flag, CancellationToken ct) { Flags.Add(flag); return Task.CompletedTask; }

    public Task AppendAuditAsync(string cycleId, AuditEntry entry, CancellationToken ct) { Audit.Add(entry); return Task.CompletedTask; }
}

public sealed class InMemoryOutbox : IVoteOutbox
{
    public readonly List<VoteSubmission> Pending = new();
    public readonly Dictionary<Guid, string> Rejected = new();

    public Task<IReadOnlyList<VoteSubmission>> PeekAsync(int max, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VoteSubmission>>(
            Pending.Where(p => !Rejected.ContainsKey(p.VoteId)).Take(max).ToList());

    public Task RemoveAsync(IEnumerable<Guid> voteIds, CancellationToken ct)
    {
        var ids = voteIds.ToHashSet();
        Pending.RemoveAll(p => ids.Contains(p.VoteId));
        return Task.CompletedTask;
    }

    public Task MarkRejectedAsync(Guid voteId, string reason, CancellationToken ct)
    {
        Rejected[voteId] = reason;
        return Task.CompletedTask;
    }

    public Task<int> PendingCountAsync(CancellationToken ct) =>
        Task.FromResult(Pending.Count(p => !Rejected.ContainsKey(p.VoteId)));
}

public sealed class FakeApi : IKioskApi
{
    public bool Offline;
    public int Calls;
    private readonly KioskVoteService _service;
    public FakeApi(KioskVoteService service) => _service = service;

    public Task<VoteBatchResponse> PostVotesAsync(VoteBatchRequest request, CancellationToken ct)
    {
        Calls++;
        if (Offline) throw new HttpRequestException("offline");
        return _service.SubmitAsync(request, ct);
    }
}

public static class Make
{
    public const string Cycle = "fall-2026";
    public static readonly DateTimeOffset T0 = new(2026, 10, 5, 19, 0, 0, TimeSpan.Zero);

    public static VoteSubmission Vote(string scout, string candidate, string race = "SPL", int round = 1,
        Guid? id = null, DateTimeOffset? castAt = null) =>
        new(id ?? Guid.NewGuid(), Cycle, scout, race, round, candidate, castAt ?? T0, "tab-1");

    public static RaceState Open(string race = "SPL", int round = 1) =>
        new(Cycle, race, round, RaceStatus.Open, null);

    public static RaceState Closed(DateTimeOffset closedAt, string race = "SPL", int round = 1) =>
        new(Cycle, race, round, RaceStatus.Closed, closedAt);
}
