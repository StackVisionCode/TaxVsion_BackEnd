using System.Diagnostics.Metrics;

namespace TaxVision.Documents.Infrastructure.Observability;

/// <summary>Métricas de baja cardinalidad del servicio Documents. Nunca agregar tenant/owner/file
/// como tags. Se amplía en la fase de observabilidad (D11).</summary>
public sealed class DocumentsMetrics : IDisposable
{
    public const string MeterName = "TaxVision.Documents";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _requested;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _failed;
    private readonly Histogram<double> _durationSeconds;

    public DocumentsMetrics()
    {
        _requested = _meter.CreateCounter<long>("document_generation_requested_total");
        _completed = _meter.CreateCounter<long>("document_generation_completed_total");
        _failed = _meter.CreateCounter<long>("document_generation_failed_total");
        _durationSeconds = _meter.CreateHistogram<double>("document_generation_duration", unit: "s");
    }

    public void RecordRequested(string documentType, string format) =>
        _requested.Add(1, Tag("document_type", documentType), Tag("format", format));

    public void RecordCompleted(string documentType, string format) =>
        _completed.Add(1, Tag("document_type", documentType), Tag("format", format));

    public void RecordFailed(string documentType, string errorCode) =>
        _failed.Add(1, Tag("document_type", documentType), Tag("error_code", errorCode));

    public void RecordDuration(double seconds, string documentType) =>
        _durationSeconds.Record(seconds, Tag("document_type", documentType));

    private static KeyValuePair<string, object?> Tag(string name, string value) => new(name, value);

    public void Dispose() => _meter.Dispose();
}
