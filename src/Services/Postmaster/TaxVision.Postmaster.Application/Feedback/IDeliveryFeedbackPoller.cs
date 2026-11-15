using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>
/// Modo pull: pregunta al proveedor qué pasó con correos pendientes. Solo lo implementa un proveedor
/// con API de consulta; uno sin webhook entra registrando solo esto.
/// </summary>
public interface IDeliveryFeedbackPoller
{
    FeedbackSource Source { get; }

    /// <summary>
    /// False si al proveedor le falta configuración (p. ej. sin API key). El poller se registra igual: la forma del
    /// contenedor no puede depender de la configuración porque el código de Wolverine se genera una sola vez.
    /// </summary>
    bool IsEnabled { get; }

    Task<IReadOnlyList<TranslatedFeedback>> PollAsync(IReadOnlyList<PendingDeliveryRef> pending, CancellationToken ct);
}
