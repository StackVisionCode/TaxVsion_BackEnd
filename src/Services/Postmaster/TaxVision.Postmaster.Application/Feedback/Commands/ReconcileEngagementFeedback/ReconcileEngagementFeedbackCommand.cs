namespace TaxVision.Postmaster.Application.Feedback.Commands.ReconcileEngagementFeedback;

/// <summary>
/// Modo pull de actividad reciente (entregas, aperturas y clics): lee la del proveedor entre <see cref="FromUtc"/> (inclusivo) y
/// <see cref="ToUtc"/> (exclusivo). Respaldo del webhook: lo que ya llegó por ahí se descarta por recibo.
/// </summary>
/// <param name="MaxEvents">Tope de eventos leídos por corrida.</param>
/// <param name="ApplyBatchSize">Avisos por transacción: un choque solo deja para la próxima vuelta su lote.</param>
public sealed record ReconcileEngagementFeedbackCommand(
    DateTime FromUtc,
    DateTime ToUtc,
    int MaxEvents,
    DateTime NowUtc,
    int ApplyBatchSize
);
