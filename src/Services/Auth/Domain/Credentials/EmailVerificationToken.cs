using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Credentials;

/// <summary>Token de verificación para cambio de email. Se envía a la dirección nueva.</summary>
public sealed class EmailVerificationToken : TenantEntity
{
    private EmailVerificationToken() { }

    [UserReference(
        UserReferenceKind.Credential,
        HandledBy = "OffboardUserHandler",
        Note = "Inerte: canjearlo exige usuario activo."
    )]
    public Guid UserId { get; private set; }
    public string NewEmail { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "Quién de la oficina pidió el cambio de correo: historia.")]
    public Guid? RequestedByUserId { get; private set; }

    /// <summary>El cambio lo pidió alguien de la oficina, no el propio usuario (D-08, D-11).</summary>
    public bool RequestedByOffice => RequestedByUserId is { } by && by != UserId;

    public bool IsUsable(DateTime utcNow) => UsedAtUtc is null && utcNow < ExpiresAtUtc;

    public static EmailVerificationToken Create(
        Guid tenantId,
        Guid userId,
        string newEmail,
        string tokenHash,
        TimeSpan validity,
        Guid? requestedByUserId = null
    )
    {
        var token = new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NewEmail = newEmail.Trim().ToLowerInvariant(),
            TokenHash = tokenHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(validity),
            RequestedByUserId = requestedByUserId,
        };
        token.SetTenant(tenantId);
        return token;
    }

    public void MarkUsed() => UsedAtUtc ??= DateTime.UtcNow;
}
