namespace TaxVision.Referrals.Application.Codes.RegenerateTenantReferralCode;

/// <summary>
/// El owner de la oficina genera (o regenera) su código de referido. Si ya tenía uno activo, queda revocado
/// ("Superseded") y no sirve para nada más; los referidos ya atribuidos conservan su premio. El programa es el
/// vigente de plataforma: el cliente no elige ni envía un ProgramId. Cada regeneración usa su propia
/// <paramref name="IdempotencyKey"/> (el token en claro se deriva de ella).
/// </summary>
public sealed record RegenerateTenantReferralCodeCommand(
    Guid TenantId,
    Guid ActorUserId,
    string IdempotencyKey,
    DateTime? ExpiresAtUtc = null
);
