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

    /// <summary>La revisión con la huella de su reserva, sin rastrear y de cualquier tenant (PlatformAdmin).</summary>
    Task<LateCommitReviewTarget?> FindTargetAsync(Guid reviewId, CancellationToken ct = default);

    /// <summary>La revisión rastreada, de cualquier tenant, para resolverla (PlatformAdmin).</summary>
    Task<LateCommitReview?> GetForResolutionAsync(Guid reviewId, CancellationToken ct = default);
}

/// <summary>Lo necesario para repetir el commit de una revisión.</summary>
public sealed record LateCommitReviewTarget(
    Guid ReviewId,
    Guid TenantId,
    Guid CodeReservationId,
    string PaymentSource,
    Guid RelatedPaymentId,
    Guid SourceEventId,
    string SnapshotHash,
    DateTime? ResolvedAtUtc
);
