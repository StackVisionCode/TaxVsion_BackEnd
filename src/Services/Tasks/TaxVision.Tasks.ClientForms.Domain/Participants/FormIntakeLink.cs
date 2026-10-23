using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Participants;

public sealed class FormIntakeLink : AggregateRoot
{
    private FormIntakeLink() { }

    public string Name { get; private set; } = string.Empty;
    public Guid FormTemplateId { get; private set; }
    public Guid FormTemplateVersionId { get; private set; }
    public Guid? EntryNoticeId { get; private set; }
    public int? EntryNoticeVersionNumber { get; private set; }
    public FormIntakeLinkStatus Status { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public string TokenLast4 { get; private set; } = string.Empty;
    public string TokenCipher { get; private set; } = string.Empty;
    public short TokenKeyVersion { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public int? MaxSubmissions { get; private set; }
    public int SubmissionCount { get; private set; }
    public Guid ResponsibleUserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<FormIntakeLink> Create(
        Guid tenantId,
        string name,
        Guid templateId,
        Guid versionId,
        Guid entryNoticeId,
        int entryNoticeVersionNumber,
        string tokenHash,
        string tokenLast4,
        string tokenCipher,
        short tokenKeyVersion,
        DateTime? expiresAtUtc,
        int? maxSubmissions,
        Guid responsibleUserId,
        Guid createdByUserId,
        DateTime nowUtc
    )
    {
        var normalizedName = name?.Trim();
        if (
            tenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(normalizedName)
            || normalizedName.Length > 120
            || templateId == Guid.Empty
            || versionId == Guid.Empty
            || entryNoticeId == Guid.Empty
            || entryNoticeVersionNumber < 1
            || responsibleUserId == Guid.Empty
            || createdByUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(tokenHash)
            || tokenLast4.Length != 4
            || string.IsNullOrWhiteSpace(tokenCipher)
            || expiresAtUtc <= nowUtc
            || maxSubmissions is <= 0
        )
            return Result.Failure<FormIntakeLink>(FormParticipantErrors.IntakeLinkInvalid);
        var link = new FormIntakeLink
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            FormTemplateId = templateId,
            FormTemplateVersionId = versionId,
            EntryNoticeId = entryNoticeId,
            EntryNoticeVersionNumber = entryNoticeVersionNumber,
            Status = FormIntakeLinkStatus.Active,
            TokenHash = tokenHash,
            TokenLast4 = tokenLast4,
            TokenCipher = tokenCipher,
            TokenKeyVersion = tokenKeyVersion,
            ExpiresAtUtc = expiresAtUtc,
            MaxSubmissions = maxSubmissions,
            ResponsibleUserId = responsibleUserId,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = nowUtc,
        };
        link.SetTenant(tenantId);
        return Result.Success(link);
    }

    public bool IsAvailableAt(DateTime nowUtc) =>
        Status == FormIntakeLinkStatus.Active
        && EntryNoticeId.HasValue
        && EntryNoticeVersionNumber.HasValue
        && (!ExpiresAtUtc.HasValue || nowUtc < ExpiresAtUtc.Value)
        && (!MaxSubmissions.HasValue || SubmissionCount < MaxSubmissions.Value);

    public Result Pause()
    {
        if (Status != FormIntakeLinkStatus.Active)
            return Result.Failure(FormParticipantErrors.IntakeLinkUnavailable);
        Status = FormIntakeLinkStatus.Paused;
        return Result.Success();
    }

    public Result Resume(DateTime nowUtc)
    {
        if (
            Status != FormIntakeLinkStatus.Paused
            || ExpiresAtUtc <= nowUtc
            || !EntryNoticeId.HasValue
            || !EntryNoticeVersionNumber.HasValue
        )
            return Result.Failure(FormParticipantErrors.IntakeLinkUnavailable);
        Status = FormIntakeLinkStatus.Active;
        return Result.Success();
    }

    public Result Revoke()
    {
        if (Status == FormIntakeLinkStatus.Revoked)
            return Result.Success();
        Status = FormIntakeLinkStatus.Revoked;
        return Result.Success();
    }

    public Result PinVersion(Guid versionId)
    {
        if (versionId == Guid.Empty || Status == FormIntakeLinkStatus.Revoked)
            return Result.Failure(FormParticipantErrors.IntakeLinkUnavailable);
        FormTemplateVersionId = versionId;
        return Result.Success();
    }

    public Result RegisterSubmission(DateTime nowUtc)
    {
        if (!IsAvailableAt(nowUtc))
            return Result.Failure(FormParticipantErrors.IntakeLimitReached);
        SubmissionCount++;
        return Result.Success();
    }

    public Result RegenerateToken(string hash, string last4, string cipher, short keyVersion)
    {
        if (
            Status == FormIntakeLinkStatus.Revoked
            || string.IsNullOrWhiteSpace(hash)
            || last4.Length != 4
            || string.IsNullOrWhiteSpace(cipher)
        )
            return Result.Failure(FormParticipantErrors.IntakeLinkInvalid);
        TokenHash = hash;
        TokenLast4 = last4;
        TokenCipher = cipher;
        TokenKeyVersion = keyVersion;
        return Result.Success();
    }
}
