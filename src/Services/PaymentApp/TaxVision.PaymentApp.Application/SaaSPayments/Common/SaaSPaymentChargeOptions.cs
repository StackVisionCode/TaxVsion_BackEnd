namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>Plazos del cobro fuera de la transacción (<c>PaymentApp:ChargeExecution</c>).</summary>
public sealed class SaaSPaymentChargeOptions
{
    public const string SectionName = "PaymentApp:ChargeExecution";

    /// <summary>Tiempo durante el que el proveedor recuerda la clave. Stripe la guarda al menos 24 h: pasado este
    /// plazo, repetir la llamada podría crear otro cobro, así que no se llama y lo cierra la reconciliación.</summary>
    public TimeSpan ProviderKeyWindow { get; set; } = TimeSpan.FromHours(23);

    /// <summary>Un pago <c>Pending</c> sin cambios por más de esto ya no tiene un comando en curso y se reconcilia.
    /// Tiene que superar de sobra el tiempo máximo de una llamada al proveedor.</summary>
    public TimeSpan PendingChargeThreshold { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Margen entre el reloj local y el del proveedor al buscar el cargo del intento.</summary>
    public TimeSpan ProviderClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    public bool IsValid() =>
        ProviderKeyWindow > TimeSpan.Zero
        && PendingChargeThreshold > TimeSpan.Zero
        && ProviderClockSkew >= TimeSpan.Zero;
}
