namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Una oficina donde el email + password fue válido. <see cref="ChallengeRequired"/>: el handoff
/// debe pedir un código. <see cref="MustEnroll"/>: exige MFA pero no hay método, se entra con flag
/// de setup.
/// </summary>
public sealed record DiscoveredOffice(Guid TenantId, Guid UserId, bool ChallengeRequired, bool MustEnroll);

/// <summary>El conjunto de oficinas autenticadas en un <c>discover-login</c>, para que el paso de
/// selección/MFA no tenga que reautenticar el password.</summary>
public sealed record DiscoverySession(IReadOnlyList<DiscoveredOffice> Offices);

/// <summary>
/// Estado efímero entre <c>discover-login</c> y la emisión del ticket, cuando hay varias oficinas o
/// MFA pendiente. Redis, TTL corto. <see cref="PeekAsync"/> (no borra) porque el MFA puede
/// reintentarse dentro de la ventana; <see cref="ConsumeAsync"/> recién cuando el ticket se emitió.
/// </summary>
public interface IDiscoverySessionStore
{
    Task<Guid> StoreAsync(DiscoverySession session, CancellationToken ct = default);
    Task<DiscoverySession?> PeekAsync(Guid reference, CancellationToken ct = default);
    Task ConsumeAsync(Guid reference, CancellationToken ct = default);
}
