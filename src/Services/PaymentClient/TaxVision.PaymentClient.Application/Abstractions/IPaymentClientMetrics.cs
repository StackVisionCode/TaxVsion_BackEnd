namespace TaxVision.PaymentClient.Application.Abstractions;

/// <summary>Catálogo de métricas custom (§29.2 del diseño) que los handlers alimentan — la
/// implementación concreta (Infrastructure) es la que sabe de OpenTelemetry/<c>Meter</c>; acá
/// solo vive el contrato.</summary>
public interface IPaymentClientMetrics
{
    /// <summary>Un cobro exitoso — GMV y count viajan juntos porque siempre se reportan en el
    /// mismo punto (payment succeeded).</summary>
    void RecordPaymentSucceeded(long amountCents, string currency);

    void RecordPlatformFee(long feeCents, string currency);

    void RecordConnectOnboardingCompleted();

    void RecordPaymentLinkCreated();

    void RecordPaymentLinkUsed();

    void RecordRefund(string provider);

    void RecordWebhookReceived(string provider);

    void RecordWebhookDuplicate(string provider);

    void RecordWebhookSignatureFailed(string provider);
}
