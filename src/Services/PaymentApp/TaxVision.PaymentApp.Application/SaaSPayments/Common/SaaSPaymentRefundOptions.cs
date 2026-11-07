namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>Plazos del reembolso de plataforma (<c>PaymentApp:Refunds</c>).</summary>
public sealed class SaaSPaymentRefundOptions
{
    public const string SectionName = "PaymentApp:Refunds";

    /// <summary>Sin <c>Idempotency-Key</c>, la misma petición (monto, motivo y admin) dentro de este plazo se toma por
    /// un reintento y no reembolsa otra vez. Pasado el plazo es una petición nueva.</summary>
    public TimeSpan DuplicateRequestWindow { get; set; } = TimeSpan.FromMinutes(15);

    public bool IsValid() => DuplicateRequestWindow > TimeSpan.Zero;
}
