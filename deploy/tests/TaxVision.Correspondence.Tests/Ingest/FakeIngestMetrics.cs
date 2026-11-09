using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Tests.Ingest;

internal sealed class FakeIngestMetrics : IIngestMetrics
{
    public List<string> RejectedCodes { get; } = [];

    public void IngestRejected(string code) => RejectedCodes.Add(code);
}
