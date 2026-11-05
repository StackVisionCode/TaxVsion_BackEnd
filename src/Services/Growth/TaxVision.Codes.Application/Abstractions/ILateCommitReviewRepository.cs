using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Codes.Application.Abstractions;

public interface ILateCommitReviewRepository
{
    /// <summary>
    /// Guarda la revisión si no existe otra con el mismo (CodeReservationId, SourceEventId); índice único
    /// detrás. Persiste por su cuenta porque el commit fallido revierte todo lo demás. False si ya existía.
    /// </summary>
    Task<bool> RecordAsync(LateCommitReview review, CancellationToken ct = default);

    /// <summary>Pendientes de todos los tenants, las más viejas primero (consulta de PlatformAdmin).</summary>
    Task<IReadOnlyList<LateCommitReview>> ListPendingAsync(int take, CancellationToken ct = default);
}
