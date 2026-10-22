namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Métricas del webhook único del proveedor de email. Sin direcciones ni ids (PII/cardinalidad).</summary>
public interface IProviderWebhookMetrics
{
    /// <summary>Un aviso que no era de campaña ni de Postmaster: otro tráfico de la misma cuenta.</summary>
    void RecordUnrouted(string source);
}
