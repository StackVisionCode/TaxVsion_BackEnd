using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Credentials;

public sealed class PasswordResetToken : TenantEntity
{
    public const int MaxAttempts = 5;

    private PasswordResetToken() { }

    [UserReference(
        UserReferenceKind.Credential,
        HandledBy = "OffboardUserHandler",
        Note = "Inerte: canjearlo exige usuario activo."
    )]
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public string? RequestedIp { get; private set; }
    public int Attempts { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    /// <summary>Anulado sin usarse; el motivo queda en <see cref="RevocationReason"/>.</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    public ResetOrigin Origin { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "Quién de la oficina pidió el link: historia.")]
    public Guid? RequestedByUserId { get; private set; }

    public ResetRevocationReason? RevocationReason { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "Quién de la oficina anuló el link: historia.")]
    public Guid? RevokedByUserId { get; private set; }

    /// <summary>EventId del correo con el link, para seguir su entrega en Notification.</summary>
    public Guid? NotificationEventId { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public bool IsUsable(DateTime utcNow) =>
        UsedAtUtc is null && RevokedAtUtc is null && utcNow < ExpiresAtUtc && Attempts < MaxAttempts;

    public static PasswordResetToken Create(
        Guid tenantId,
        Guid userId,
        string tokenHash,
        string? requestedIp,
        TimeSpan validity,
        ResetOrigin origin = ResetOrigin.SelfService,
        Guid? requestedByUserId = null,
        Guid? notificationEventId = null
    )
    {
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            RequestedIp = requestedIp is { Length: > 45 } ? requestedIp[..45] : requestedIp,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(validity),
            Origin = origin,
            RequestedByUserId = requestedByUserId,
            NotificationEventId = notificationEventId,
        };
        token.SetTenant(tenantId);
        return token;
    }

    public void RegisterAttempt() => Attempts++;

    public void MarkUsed() => UsedAtUtc ??= DateTime.UtcNow;

    /// <summary>Anula el enlace si sigue sin usarse (I-5); uno ya usado o ya anulado conserva su estado.</summary>
    public void Revoke(DateTime utcNow, ResetRevocationReason reason, Guid? byUserId = null)
    {
        if (UsedAtUtc is not null || RevokedAtUtc is not null)
            return;
        RevokedAtUtc = utcNow;
        RevocationReason = reason;
        RevokedByUserId = byUserId;
    }

    public PasswordResetTokenStatus StatusAt(DateTime utcNow)
    {
        if (UsedAtUtc is not null)
            return PasswordResetTokenStatus.Completed;
        if (RevokedAtUtc is not null)
            return RevocationReason switch
            {
                ResetRevocationReason.Superseded => PasswordResetTokenStatus.Superseded,
                ResetRevocationReason.CancelledByOffice => PasswordResetTokenStatus.Cancelled,
                ResetRevocationReason.AccountSecured => PasswordResetTokenStatus.AccountSecured,
                _ => PasswordResetTokenStatus.PasswordChanged,
            };
        if (utcNow >= ExpiresAtUtc)
            return PasswordResetTokenStatus.Expired;
        return Attempts >= MaxAttempts ? PasswordResetTokenStatus.Exhausted : PasswordResetTokenStatus.Pending;
    }
}
