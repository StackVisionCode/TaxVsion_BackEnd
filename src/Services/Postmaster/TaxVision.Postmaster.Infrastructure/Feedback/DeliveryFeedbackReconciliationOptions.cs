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

    /// <summary>Avisos por transacción al aplicar lo consultado: un choque solo deja para la próxima vuelta su lote.</summary>
    public int ApplyBatchSize { get; set; } = 50;

    /// <summary>Tope de <see cref="MaxEngagementEventsPerRun"/>: diez páginas de la API mantienen la vuelta corta.</summary>
    public const int MaxEngagementEventsPerRunLimit = 10_000;

    /// <summary>Aperturas y clics por rango en la misma vuelta, después de las entregas. false = solo webhook.</summary>
    public bool EngagementEnabled { get; set; } = true;

    /// <summary>Ventana hacia atrás que se relee en cada vuelta; lo ya aplicado se descarta por recibo.</summary>
    public int EngagementLookbackHours { get; set; } = 48;

    /// <summary>Eventos leídos por vuelta (páginas de hasta 1000); lo que pase queda para la próxima.</summary>
    public int MaxEngagementEventsPerRun { get; set; } = 2000;
}
