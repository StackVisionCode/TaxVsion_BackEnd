using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>
/// Modo pull de actividad reciente (entregas, aperturas y clics): el proveedor la lista por rango de fechas, no por correo. Puede traer
/// correos de otros entornos que comparten la cuenta; filtrarlos es trabajo del handler.
/// </summary>
public interface IEngagementFeedbackPoller
{
    FeedbackSource Source { get; }

    /// <param name="fromUtc">Inclusivo.</param>
    /// <param name="toUtc">Exclusivo.</param>
    /// <param name="maxEvents">Tope de eventos por corrida; lo que pase queda para la próxima.</param>
    Task<IReadOnlyList<TranslatedFeedback>> PollEngagementAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int maxEvents,
        CancellationToken ct
    );
}
