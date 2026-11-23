using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>La política de sesión de la plataforma (una sola fila). Null = todavía no se guardó: rigen los valores por defecto.</summary>
public interface ISessionPolicyRepository
{
    Task<PlatformSessionPolicy?> GetAsync(CancellationToken ct = default);

    Task AddAsync(PlatformSessionPolicy policy, CancellationToken ct = default);
}

/// <summary>
/// Decide, al renovar, si una sesión debe terminar por la política de la plataforma. Lee la política con una caché
/// corta: se consulta en cada refresh y cambia muy poco.
/// </summary>
public interface ISessionLifetimeGate
{
    /// <summary>"idle_timeout", "absolute_lifetime" o null si la sesión sigue valiendo.</summary>
    Task<string?> EndReasonAsync(UserSession session, CancellationToken ct = default);

    /// <summary>Descarta la política en caché para que el siguiente refresh lea la recién guardada.</summary>
    void Invalidate();
}
