using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Infrastructure.Observability;

internal sealed class CorrespondenceIngestMetrics : IIngestMetrics
{
    public void IngestRejected(string code) =>
        CorrespondenceMetrics.IngestRejectedTotal.Add(1, new KeyValuePair<string, object?>("code", code));
}
