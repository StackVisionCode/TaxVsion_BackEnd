using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.OAuth;

/// <summary>Datos del intento de conectar cuenta que originó un <c>state</c> — recuperados en el callback via <see cref="IOAuthConnectStateStore.ConsumeAsync"/>.</summary>
public sealed record OAuthConnectState(Guid TenantId, ProviderCode ProviderCode, Guid InitiatedByUserId);

/// <summary>
/// CSRF + single-use para el flujo de conectar cuenta (D3 §12.3): el <c>state</c> es un token opaco
/// (nunca autocontenido — un state falsificado simplemente no matchea nada acá y el callback lo
/// rechaza). TTL 10 minutos, consumido exactamente una vez.
/// </summary>
public interface IOAuthConnectStateStore
{
    Task<string> CreateAsync(
        Guid tenantId,
        ProviderCode providerCode,
        Guid initiatedByUserId,
        CancellationToken ct = default
    );

    /// <summary>Devuelve el estado y lo borra atómicamente — un mismo <paramref name="state"/> nunca puede consumirse dos veces. Null si no existe, expiró, o ya fue consumido.</summary>
    Task<OAuthConnectState?> ConsumeAsync(string state, CancellationToken ct = default);
}
