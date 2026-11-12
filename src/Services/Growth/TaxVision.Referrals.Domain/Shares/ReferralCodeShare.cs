using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Common;

namespace TaxVision.Referrals.Domain.Shares;

/// <summary>
/// Registro append-only de cada vez que alguien de la oficina copia o comparte el código
/// (quién, por qué canal, cuándo). Cualquier usuario con acceso de lectura puede registrar un envío
/// — compartir no modifica el código —, pero solo el owner genera/regenera. Los envíos hechos con un
/// código ya revocado se conservan: los registros que produjeron mantienen su premio.
/// </summary>
public sealed class ReferralCodeShare : TenantEntity
{
    public Guid ReferralCodeId { get; private set; }
    public ReferralShareChannel Channel { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid SharedByUserId { get; private set; }
    public DateTime SharedAtUtc { get; private set; }

    /// <summary>Pista opcional del destinatario (p. ej. un email o teléfono enmascarado). Nunca el dato completo de un tercero.</summary>
    public string? RecipientHint { get; private set; }

    private ReferralCodeShare() { }

    public static Result<ReferralCodeShare> Record(
        ReferralCode code,
        ReferralShareChannel channel,
        Guid sharedByUserId,
        string? recipientHint,
        DateTime nowUtc
    )
    {
        var actor = DomainGuards.EnsureActor(sharedByUserId);
        if (actor.IsFailure)
            return Result.Failure<ReferralCodeShare>(actor.Error);

        if (!Enum.IsDefined(channel))
            return Result.Failure<ReferralCodeShare>(
                new Error("ReferralShare.InvalidChannel", "Channel is not supported.")
            );

        // Un código revocado/expirado ya no se comparte: el envío no produciría un registro válido.
        var usable = code.EnsureUsable(nowUtc);
        if (usable.IsFailure)
            return Result.Failure<ReferralCodeShare>(usable.Error);

        var hint = string.IsNullOrWhiteSpace(recipientHint) ? null : recipientHint.Trim();
        if (hint is { Length: > 200 })
            return Result.Failure<ReferralCodeShare>(
                new Error("ReferralShare.InvalidRecipientHint", "RecipientHint must be 200 characters or fewer.")
            );

        var share = new ReferralCodeShare
        {
            ReferralCodeId = code.Id,
            Channel = channel,
            SharedByUserId = sharedByUserId,
            SharedAtUtc = nowUtc,
            RecipientHint = hint,
        };
        share.SetTenant(code.TenantId);
        return Result.Success(share);
    }
}
