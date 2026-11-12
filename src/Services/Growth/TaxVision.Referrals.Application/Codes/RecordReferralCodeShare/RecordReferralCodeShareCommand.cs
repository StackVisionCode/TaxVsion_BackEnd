using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Referrals.Application.Codes.RecordReferralCodeShare;

/// <summary>
/// Registra que alguien de la oficina copió o compartió el código por un canal. Compartir NO modifica el código:
/// cualquier usuario con acceso de lectura puede hacerlo. El Id devuelto viaja en el enlace (<c>sid</c>) para que la
/// atribución del referido quede con el canal real.
/// </summary>
public sealed record RecordReferralCodeShareCommand(
    Guid TenantId,
    Guid ReferralCodeId,
    ReferralShareChannel Channel,
    Guid UserId,
    string? RecipientHint
);

public sealed record ReferralCodeShareResult(
    Guid ShareId,
    Guid ReferralCodeId,
    ReferralShareChannel Channel,
    DateTime SharedAtUtc
);
