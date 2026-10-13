using BuildingBlocks.Messaging.PlatformIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.TenantDomains.IntegrationEvents;

/// <summary>
/// Cambió el dominio de la plataforma: re-apunta los hosts guardados de cada oficina y el del tenant
/// Platform. Usa los valores del evento y no <c>IPlatformSettings.Current</c>, para no depender de
/// en qué orden corre frente al handler compartido que actualiza esa versión.
/// </summary>
public static class PlatformDomainChangedConsumer
{
    public static Task Handle(
        PlatformSettingsChangedIntegrationEvent evt,
        ITenantDomainRebaser rebaser,
        CancellationToken ct
    ) => rebaser.RebaseAsync(evt.BaseDomain, evt.ApiHost, ct);
}
