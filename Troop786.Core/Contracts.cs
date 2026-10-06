namespace Troop786.Core;

/// <summary>
/// One ballot choice cast on a tablet. VoteId is generated on the tablet and makes uploads idempotent.
/// CastAt must be server-corrected time (device clock plus the offset delivered in the cycle bundle).
/// </summary>
public sealed record VoteSubmission(
    Guid VoteId,
    string CycleId,
    string ScoutId,
    string RaceId,        // "SPL", "PL-EAGLE", ...
    int Round,            // 1 = first round, 2+ = runoffs (each round has its own vote records)
    string CandidateId,
    DateTimeOffset CastAt,
    string DeviceId);

public enum VoteOutcome { Accepted, Duplicate, Rejected }

public sealed record VoteResult(Guid VoteId, VoteOutcome Outcome, string? Reason = null);

public sealed record VoteBatchRequest(IReadOnlyList<VoteSubmission> Votes);

public sealed record VoteBatchResponse(IReadOnlyList<VoteResult> Results);

public enum RaceStatus { Open, Closed, Tie, Elected }

/// <summary>State of one race round. ClosedAt is set once the round is no longer Open.</summary>
public sealed record RaceState(
    string CycleId,
    string RaceId,
    int Round,
    RaceStatus Status,
    DateTimeOffset? ClosedAt);

public sealed record VoteRecord(VoteSubmission Vote, DateTimeOffset ReceivedAt);

/// <summary>A later vote from the same scout in the same race round. Stored, never counted.</summary>
public sealed record VoteFlag(
    VoteSubmission Rejected,
    Guid ExistingVoteId,
    string Reason,
    DateTimeOffset At);

public sealed record AuditEntry(DateTimeOffset At, string Actor, string Action, string Detail);
