using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Domain.Participants;

public sealed class FormAccessChallenge : AggregateRoot
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(30);

    private FormAccessChallenge() { }

    public Guid? GrantId { get; private set; }
    public Guid? IntakeLinkId { get; private set; }

    // El email vive solo cifrado; la búsqueda usa un HMAC determinista por tenant.
    public string EmailCipher { get; private set; } = string.Empty;
    public short EmailKeyVersion { get; private set; }
    public string EmailLookupHash { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    public Guid? EntryNoticeId { get; private set; }
    public int? EntryNoticeVersionNumber { get; private set; }
    public string? EntryNoticeContentHash { get; private set; }
    public DateTime IssuedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? BlockedUntilUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }

    // Control optimista: dos intentos en paralelo no pueden pisar el contador.
    public byte[] RowVersion { get; private set; } = [];

    public static Result<FormAccessChallenge> Create(
        Guid tenantId,
        Guid? grantId,
        Guid? intakeLinkId,
        string emailCipher,
        short emailKeyVersion,
        string emailLookupHash,
        string codeHash,
        ConsentNoticeVersion entryNoticeVersion,
        DateTime expiresAtUtc,
        DateTime nowUtc
    )
    {
        if (
            tenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(emailCipher)
            || string.IsNullOrWhiteSpace(emailLookupHash)
            || string.IsNullOrWhiteSpace(codeHash)
            || expiresAtUtc <= nowUtc
            || (grantId.HasValue == intakeLinkId.HasValue)
            || grantId == Guid.Empty
            || intakeLinkId == Guid.Empty
            || entryNoticeVersion.TenantId != tenantId
        )
            return Result.Failure<FormAccessChallenge>(FormParticipantErrors.ChallengeInvalid);
        var challenge = new FormAccessChallenge
        {
            Id = Guid.NewGuid(),
            GrantId = grantId,
            IntakeLinkId = intakeLinkId,
            EmailCipher = emailCipher,
            EmailKeyVersion = emailKeyVersion,
            EmailLookupHash = emailLookupHash,
            CodeHash = codeHash,
            EntryNoticeId = entryNoticeVersion.ConsentNoticeId,
            EntryNoticeVersionNumber = entryNoticeVersion.VersionNumber,
            EntryNoticeContentHash = entryNoticeVersion.ContentHash,
            IssuedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
        };
        challenge.SetTenant(tenantId);
        return Result.Success(challenge);
    }

    public bool IsBlockedAt(DateTime nowUtc) => BlockedUntilUtc.HasValue && nowUtc < BlockedUntilUtc.Value;

    // Un bloqueo vigente impide emitir otro código: si no, cada reemisión regalaría 5 intentos más.
    public bool AllowsReissueAt(DateTime nowUtc) => !IsBlockedAt(nowUtc) && nowUtc >= IssuedAtUtc.Add(ResendCooldown);

    public Result Verify(bool codeMatches, DateTime nowUtc)
    {
        if (ConsumedAtUtc.HasValue)
            return Result.Failure(FormParticipantErrors.ChallengeConsumed);
        if (nowUtc >= ExpiresAtUtc)
            return Result.Failure(FormParticipantErrors.ChallengeExpired);
        if (IsBlockedAt(nowUtc))
            return Result.Failure(FormParticipantErrors.ChallengeBlocked);
        if (BlockedUntilUtc.HasValue)
        {
            BlockedUntilUtc = null;
            Attempts = 0;
        }
        if (!codeMatches)
        {
            Attempts++;
            if (Attempts >= MaxAttempts)
                BlockedUntilUtc = nowUtc.Add(BlockDuration);
            return Result.Failure(FormParticipantErrors.ChallengeCodeMismatch);
        }
        ConsumedAtUtc = nowUtc;
        return Result.Success();
    }
}
