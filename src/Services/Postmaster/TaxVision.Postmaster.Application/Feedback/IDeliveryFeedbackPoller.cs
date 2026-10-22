using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>
/// Modo pull: pregunta al proveedor qué pasó con correos pendientes. Solo lo implementa un proveedor
/// con API de consulta; uno sin webhook entra registrando solo esto.
/// </summary>
public interface IDeliveryFeedbackPoller
{
    FeedbackSource Source { get; }

    Task<IReadOnlyList<TranslatedFeedback>> PollAsync(IReadOnlyList<PendingDeliveryRef> pending, CancellationToken ct);
}
