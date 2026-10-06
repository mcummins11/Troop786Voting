using System.Text.Json;
using SQLite;
using Troop786.Core;

namespace Troop786.Tablet.Services;

/// <summary>Durable local vote queue. Survives app restarts and tablet reboots while offline.</summary>
public sealed class SqliteVoteOutbox : IVoteOutbox
{
    [Table("OutboxVote")]
    public sealed class Row
    {
        [PrimaryKey] public string VoteId { get; set; } = "";
        [Indexed] public long QueuedAtTicks { get; set; }
        public string Json { get; set; } = "";
        public string? RejectedReason { get; set; }
    }

    private readonly SQLiteAsyncConnection _db;
    private Task? _init;

    public SqliteVoteOutbox(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    private Task EnsureAsync() => _init ??= _db.CreateTableAsync<Row>();

    /// <summary>Called right after a scout submits. The vote is on disk before the confirmation screen shows.</summary>
    public async Task EnqueueAsync(VoteSubmission vote)
    {
        await EnsureAsync();
        await _db.InsertOrReplaceAsync(new Row
        {
            VoteId = vote.VoteId.ToString(),
            QueuedAtTicks = DateTimeOffset.UtcNow.UtcTicks,
            Json = JsonSerializer.Serialize(vote)
        });
    }

    public async Task<IReadOnlyList<VoteSubmission>> PeekAsync(int max, CancellationToken ct)
    {
        await EnsureAsync();
        var rows = await _db.Table<Row>()
            .Where(r => r.RejectedReason == null)
            .OrderBy(r => r.QueuedAtTicks)
            .Take(max)
            .ToListAsync();
        return rows.Select(r => JsonSerializer.Deserialize<VoteSubmission>(r.Json)!).ToList();
    }

    public async Task RemoveAsync(IEnumerable<Guid> voteIds, CancellationToken ct)
    {
        await EnsureAsync();
        foreach (var id in voteIds)
            await _db.DeleteAsync<Row>(id.ToString());
    }

    public async Task MarkRejectedAsync(Guid voteId, string reason, CancellationToken ct)
    {
        await EnsureAsync();
        var row = await _db.FindAsync<Row>(voteId.ToString());
        if (row is null) return;
        row.RejectedReason = reason;
        await _db.UpdateAsync(row);
    }

    public async Task<int> PendingCountAsync(CancellationToken ct)
    {
        await EnsureAsync();
        return await _db.Table<Row>().Where(r => r.RejectedReason == null).CountAsync();
    }
}
