namespace TaxVision.Postmaster.Application.Feedback.Commands.ReconcileDeliveryFeedback;

/// <summary>
/// Modo pull: pregunta al proveedor por los correos que salieron entre <see cref="SentFromUtc"/> y
/// <see cref="SentToUtc"/> y siguen sin resultado final. Respaldo del webhook, no su reemplazo.
/// </summary>
/// <param name="MaxRecipients">Tope por corrida: cada uno es una consulta al proveedor, con cupo por minuto.</param>
public sealed record ReconcileDeliveryFeedbackCommand(
    DateTime SentFromUtc,
    DateTime SentToUtc,
    int MaxRecipients,
    DateTime NowUtc
);
