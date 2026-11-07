namespace TaxVision.PaymentClient.Infrastructure.Scheduling;

/// <summary>
/// Revisión periódica de pagos que siguen esperando al proveedor (<c>Processing</c>/<c>RequiresAction</c>), y de los
/// <c>Pending</c> cuyo cobro no dejó resultado (PCL-8).
/// Un pago entra si no cambió hace más de <see cref="StuckAfterMinutes"/> (el webhook ya tuvo tiempo) y
/// se creó hace menos de <see cref="GiveUpAfterDays"/> (más viejo se deja como está, sin inventar nada).
/// </summary>
public sealed class StuckPaymentReconciliationOptions
{
    public const string SectionName = "PaymentClient:StuckPaymentReconciliation";

    /// <summary>Tope de <see cref="BatchSize"/>: cada pago es una llamada al proveedor.</summary>
    public const int MaxBatchSize = 200;

    /// <summary>false = el job no arranca (los webhooks siguen funcionando).</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 15;

    public int StuckAfterMinutes { get; set; } = 30;

    public int GiveUpAfterDays { get; set; } = 30;

    /// <summary>Pagos consultados por vuelta; lo que quede entra en la siguiente.</summary>
    public int BatchSize { get; set; } = 50;
}
