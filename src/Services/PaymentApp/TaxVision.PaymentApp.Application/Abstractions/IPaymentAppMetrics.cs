namespace TaxVision.PaymentApp.Application.Abstractions;

/// <summary>Catálogo de métricas custom (§29.2 del diseño) que los handlers alimentan — la
/// implementación concreta (Infrastructure) es la que sabe de OpenTelemetry/<c>Meter</c>; acá
/// solo vive el contrato.</summary>
public interface IPaymentAppMetrics
{
    void RecordAttempted(string provider, string type);
    void RecordSucceeded(string provider, string type);
    void RecordFailed(string provider, string type, string failureCode);
    void RecordRefunded(string provider);
    void RecordChargedBack(string provider);
    void RecordWebhookReceived(string provider);
    void RecordWebhookDuplicate(string provider);
    void RecordWebhookSignatureFailed(string provider);
    void RecordProviderLatency(double milliseconds, string provider, string method);
}
