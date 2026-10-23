using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Participants;

public sealed class FormAccessGrant : AggregateRoot
{
    public const int MaxIssues = 5;

    private FormAccessGrant() { }

    public Guid FormRequestId { get; private set; }
    public Guid? EntryNoticeId { get; private set; }
    public int? EntryNoticeVersionNumber { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public string TokenLast4 { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public DateTime? LastUsedAtUtc { get; private set; }
    public int IssueCount { get; private set; }
    public DateTime IssuedAtUtc { get; private set; }

    public static Result<FormAccessGrant> Create(
        Guid tenantId,
        Guid formRequestId,
        Guid entryNoticeId,
        int entryNoticeVersionNumber,
        string tokenHash,
        string tokenLast4,
        DateTime expiresAtUtc,
        DateTime nowUtc
    )
    {
        if (
            tenantId == Guid.Empty
            || formRequestId == Guid.Empty
            || entryNoticeId == Guid.Empty
            || entryNoticeVersionNumber < 1
            || string.IsNullOrWhiteSpace(tokenHash)
            || tokenLast4.Length != 4
            || expiresAtUtc <= nowUtc
        )
            return Result.Failure<FormAccessGrant>(FormParticipantErrors.AccessGrantInvalid);
        var grant = new FormAccessGrant
        {
            Id = Guid.NewGuid(),
            FormRequestId = formRequestId,
            EntryNoticeId = entryNoticeId,
            EntryNoticeVersionNumber = entryNoticeVersionNumber,
            TokenHash = tokenHash,
            TokenLast4 = tokenLast4,
            ExpiresAtUtc = expiresAtUtc,
            IssueCount = 1,
            IssuedAtUtc = nowUtc,
        };
        grant.SetTenant(tenantId);
        return Result.Success(grant);
    }

    public bool IsAvailableAt(DateTime nowUtc) =>
        !RevokedAtUtc.HasValue && EntryNoticeId.HasValue && EntryNoticeVersionNumber.HasValue && nowUtc < ExpiresAtUtc;

    public Result Rotate(string tokenHash, string tokenLast4, DateTime expiresAtUtc, DateTime nowUtc)
    {
        if (RevokedAtUtc.HasValue || IssueCount >= MaxIssues)
            return Result.Failure(FormParticipantErrors.AccessGrantIssueLimit);
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenLast4.Length != 4 || expiresAtUtc <= nowUtc)
            return Result.Failure(FormParticipantErrors.AccessGrantInvalid);
        TokenHash = tokenHash;
        TokenLast4 = tokenLast4;
        ExpiresAtUtc = expiresAtUtc;
        LastUsedAtUtc = null;
        IssuedAtUtc = nowUtc;
        IssueCount++;
        return Result.Success();
    }

    public Result RecordUsed(DateTime nowUtc)
    {
        if (!IsAvailableAt(nowUtc))
            return Result.Failure(FormParticipantErrors.AccessGrantUnavailable);
        LastUsedAtUtc = nowUtc;
        return Result.Success();
    }

    public void Revoke(DateTime nowUtc) => RevokedAtUtc ??= nowUtc;
}
