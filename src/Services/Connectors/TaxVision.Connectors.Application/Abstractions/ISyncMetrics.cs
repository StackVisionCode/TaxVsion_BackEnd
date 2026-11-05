using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Abstractions;

/// <summary>Métricas del sync que la Application no puede emitir sola (el Meter vive en Infrastructure).</summary>
public interface ISyncMetrics
{
    /// <summary>connectors_sync_message_abandoned_total, tag "provider".</summary>
    void MessageAbandoned(ProviderCode providerCode);
}
