using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.Observability;

internal sealed class ConnectorsSyncMetrics : ISyncMetrics
{
    public void MessageAbandoned(ProviderCode providerCode) =>
        ConnectorsMetrics.SyncMessagesAbandoned.Add(
            1,
            new KeyValuePair<string, object?>("provider", providerCode.ToString())
        );
}
