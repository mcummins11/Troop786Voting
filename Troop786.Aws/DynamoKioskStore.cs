using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Troop786.Core;

namespace Troop786.Aws;

/// <summary>
/// Kiosk items in the existing single table (no GSIs).
///   PK = CYCLE#{cycleId}
///   SK = VOTE#{race}#R{round}#{scoutId}                  one vote per scout per race round
///        RACE#{race}#R{round}                            race round state
///        FLAG#{race}#R{round}#{scoutId}#{voteId}         rejected duplicate, kept for review
///        AUDIT#{utcTicks:D20}#{guid}                     admin actions
/// </summary>
public sealed class DynamoKioskStore : IKioskStore
{
    private readonly IAmazonDynamoDB _db;
    private readonly string _table;

    public DynamoKioskStore(IAmazonDynamoDB db, string tableName)
    {
        _db = db;
        _table = tableName;
    }

    private static string Pk(string cycleId) => $"CYCLE#{cycleId}";
    private static string VoteSk(string race, int round, string scout) => $"VOTE#{race}#R{round}#{scout}";
    private static string RaceSk(string race, int round) => $"RACE#{race}#R{round}";

    private static AttributeValue S(string v) => new() { S = v };
    private static AttributeValue N(int v) => new() { N = v.ToString(CultureInfo.InvariantCulture) };
    private static string Iso(DateTimeOffset d) => d.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseIso(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public async Task<RaceState?> GetRaceAsync(string cycleId, string raceId, int round, CancellationToken ct)
    {
        var resp = await _db.GetItemAsync(new GetItemRequest
        {
            TableName = _table,
            Key = new() { ["PK"] = S(Pk(cycleId)), ["SK"] = S(RaceSk(raceId, round)) },
            ConsistentRead = true
        }, ct);

        if (resp.Item is null || resp.Item.Count == 0) return null;

        var status = Enum.Parse<RaceStatus>(resp.Item["Status"].S);
        DateTimeOffset? closedAt = resp.Item.TryGetValue("ClosedAt", out var c) && !string.IsNullOrEmpty(c.S)
            ? ParseIso(c.S)
            : null;
        return new RaceState(cycleId, raceId, round, status, closedAt);
    }

    public async Task<bool> TryPutVoteAsync(VoteRecord record, CancellationToken ct)
    {
        try
        {
            await _db.PutItemAsync(new PutItemRequest
            {
                TableName = _table,
                Item = VoteItem(record),
                ConditionExpression = "attribute_not_exists(PK)"
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<VoteRecord?> GetVoteAsync(
        string cycleId, string raceId, int round, string scoutId, CancellationToken ct)
    {
        var resp = await _db.GetItemAsync(new GetItemRequest
        {
            TableName = _table,
            Key = new() { ["PK"] = S(Pk(cycleId)), ["SK"] = S(VoteSk(raceId, round, scoutId)) },
            ConsistentRead = true
        }, ct);

        if (resp.Item is null || resp.Item.Count == 0) return null;
        var i = resp.Item;
        var vote = new VoteSubmission(
            Guid.Parse(i["VoteId"].S), cycleId, scoutId, raceId, round,
            i["CandidateId"].S, ParseIso(i["CastAt"].S), i["DeviceId"].S);
        return new VoteRecord(vote, ParseIso(i["ReceivedAt"].S));
    }

    public async Task<bool> DeleteVoteAsync(
        string cycleId, string raceId, int round, string scoutId, CancellationToken ct)
    {
        var resp = await _db.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _table,
            Key = new() { ["PK"] = S(Pk(cycleId)), ["SK"] = S(VoteSk(raceId, round, scoutId)) },
            ReturnValues = ReturnValue.ALL_OLD
        }, ct);
        return resp.Attributes is { Count: > 0 };
    }

    public async Task PutFlagAsync(VoteFlag flag, CancellationToken ct)
    {
        var v = flag.Rejected;
        await _db.PutItemAsync(new PutItemRequest
        {
            TableName = _table,
            Item = new()
            {
                ["PK"] = S(Pk(v.CycleId)),
                ["SK"] = S($"FLAG#{v.RaceId}#R{v.Round}#{v.ScoutId}#{v.VoteId}"),
                ["VoteId"] = S(v.VoteId.ToString()),
                ["ExistingVoteId"] = S(flag.ExistingVoteId.ToString()),
                ["CandidateId"] = S(v.CandidateId),
                ["CastAt"] = S(Iso(v.CastAt)),
                ["DeviceId"] = S(v.DeviceId),
                ["Reason"] = S(flag.Reason),
                ["At"] = S(Iso(flag.At))
            }
        }, ct);
    }

    public async Task AppendAuditAsync(string cycleId, AuditEntry entry, CancellationToken ct)
    {
        await _db.PutItemAsync(new PutItemRequest
        {
            TableName = _table,
            Item = new()
            {
                ["PK"] = S(Pk(cycleId)),
                ["SK"] = S($"AUDIT#{entry.At.UtcTicks:D20}#{Guid.NewGuid():N}"),
                ["Actor"] = S(entry.Actor),
                ["Action"] = S(entry.Action),
                ["Detail"] = S(entry.Detail),
                ["At"] = S(Iso(entry.At))
            }
        }, ct);
    }

    private static Dictionary<string, AttributeValue> VoteItem(VoteRecord r)
    {
        var v = r.Vote;
        return new()
        {
            ["PK"] = S(Pk(v.CycleId)),
            ["SK"] = S(VoteSk(v.RaceId, v.Round, v.ScoutId)),
            ["VoteId"] = S(v.VoteId.ToString()),
            ["CandidateId"] = S(v.CandidateId),
            ["CastAt"] = S(Iso(v.CastAt)),
            ["ReceivedAt"] = S(Iso(r.ReceivedAt)),
            ["DeviceId"] = S(v.DeviceId),
            ["Round"] = N(v.Round)
        };
    }
}
