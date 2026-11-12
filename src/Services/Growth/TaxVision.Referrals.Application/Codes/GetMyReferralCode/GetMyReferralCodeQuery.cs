using TaxVision.Referrals.Domain.Codes;

namespace TaxVision.Referrals.Application.Codes.GetMyReferralCode;

/// <summary>El código ACTIVO de la oficina, para verlo, copiarlo y compartirlo. Lo puede leer cualquier usuario con acceso de lectura; solo el owner lo regenera.</summary>
public sealed record GetMyReferralCodeQuery(Guid TenantId);

/// <summary>
/// <paramref name="IdempotencyKey"/> es la semilla con la que se deriva el token en claro en el borde HTTP (el token
/// nunca se persiste); NO debe serializarse a la respuesta.
/// </summary>
public sealed record MyReferralCodeResult(
    Guid ReferralCodeId,
    Guid ProgramId,
    ReferralCodeStatus Status,
    string DisplayPrefix,
    string LastFour,
    DateTime ExpiresAtUtc,
    string IdempotencyKey
);
