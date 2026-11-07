using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Codes.Application.Reservations.LateCommitReviews;

/// <summary>
/// GRW-1: PlatformAdmin da por bueno un canje tardío. Repite el commit de la reserva con su tenant: solo prospera si el
/// dueño del pago lo confirma (verificador real). Después cierra la revisión con el actor y la nota, auditado. El
/// orquestador vive en el Api (Application no conoce el bus).
/// </summary>
public sealed record ForceCommitLateReservationCommand(Guid ReviewId, Guid ActorUserId, string? Note);

public sealed record ForceCommitLateReservationResponse(Guid ReviewId, Guid ReservationId, Guid RedemptionId);

/// <summary>Cierra la revisión ya comprometida, en su propia transacción.</summary>
public sealed record ResolveLateCommitReviewCommand(Guid ReviewId, Guid ActorUserId, string? Note, Guid RedemptionId);

public static class ResolveLateCommitReviewHandler
{
    public static async Task<Result> Handle(
        ResolveLateCommitReviewCommand command,
        ILateCommitReviewRepository reviews,
        ICodesAuditTrail audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var review = await reviews.GetForResolutionAsync(command.ReviewId, ct);
        if (review is null)
            return Result.Failure(new Error("Codes.LateCommitReview.NotFound", "Late commit review was not found."));

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var before = new { review.ErrorCode, review.ResolvedAtUtc };
        var resolved = review.Resolve(LateCommitReview.ResolutionCommitted, command.ActorUserId, command.Note, nowUtc);
        if (resolved.IsFailure)
            return resolved;

        audit.Append(
            new CodesAuditRecord(
                PlatformTenant.Id,
                nameof(LateCommitReview),
                review.Id,
                "LateCommitForceCommitted",
                command.ActorUserId,
                "PlatformAdmin",
                before,
                new
                {
                    review.TenantId,
                    review.CodeReservationId,
                    review.PaymentSource,
                    review.RelatedPaymentId,
                    command.RedemptionId,
                    review.Resolution,
                    review.ResolvedAtUtc,
                },
                review.ResolutionNote,
                nowUtc
            )
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
