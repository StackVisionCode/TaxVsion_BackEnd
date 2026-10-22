namespace TaxVision.Postmaster.Infrastructure.Feedback;

/// <summary>
/// Cada cuánto y sobre qué correos corre la consulta de respaldo. Un correo se consulta si salió hace
/// más de <see cref="ReconcileAfterHours"/> (el webhook ya tuvo tiempo) y menos de
/// <see cref="GiveUpAfterHours"/> (más viejo ya no cambia).
/// </summary>
public sealed class DeliveryFeedbackReconciliationOptions
{
    public const string SectionName = "DeliveryFeedback";

    public int IntervalMinutes { get; set; } = 30;
    public int ReconcileAfterHours { get; set; } = 6;
    public int GiveUpAfterHours { get; set; } = 72;

    /// <summary>Cada destinatario es una consulta a la API (60/min): 50 entran en ~1 minuto.</summary>
    public int MaxRecipientsPerRun { get; set; } = 50;
}
