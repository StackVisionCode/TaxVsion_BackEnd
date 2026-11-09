namespace TaxVision.Billing.Application.Abstractions;

/// <summary>Métricas de Billing que registra la capa de aplicación.</summary>
public interface IBillingMetrics
{
    void RecordOverpayment();

    void RecordPaymentRejected(string reason);

    void RecordStockSyncInsufficient();
}
