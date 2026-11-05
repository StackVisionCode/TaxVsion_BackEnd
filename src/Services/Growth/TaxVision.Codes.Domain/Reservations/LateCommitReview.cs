using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Reservations;

/// <summary>
/// Pago exitoso que llegó cuando la reserva ya estaba vencida y no se pudo verificar contra Payment.
/// Queda pendiente para que PlatformAdmin lo resuelva a mano (hasta que exista la verificación real).
/// Única por (CodeReservationId, SourceEventId).
/// </summary>
public sealed class LateCommitReview : TenantEntity
{
    public Guid CodeReservationId { get; private set; }
    public string PaymentSource { get; private set; } = default!;
    public Guid RelatedPaymentId { get; private set; }
    public Guid SourceEventId { get; private set; }
    public string ErrorCode { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public string? Resolution { get; private set; }

    private LateCommitReview() { }

    public static Result<LateCommitReview> Record(
        Guid tenantId,
        Guid codeReservationId,
        PaymentReference payment,
        Guid sourceEventId,
        string errorCode,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty || codeReservationId == Guid.Empty || sourceEventId == Guid.Empty)
            return Result.Failure<LateCommitReview>(
                new Error("Codes.LateCommitReview.Invalid", "Tenant, reservation and source event are required.")
            );

        if (string.IsNullOrWhiteSpace(errorCode) || errorCode.Length > 200)
            return Result.Failure<LateCommitReview>(
                new Error("Codes.LateCommitReview.InvalidErrorCode", "ErrorCode is required (max 200).")
            );

        var review = new LateCommitReview
        {
            CodeReservationId = codeReservationId,
            PaymentSource = payment.Source,
            RelatedPaymentId = payment.PaymentId,
            SourceEventId = sourceEventId,
            ErrorCode = errorCode,
            CreatedAtUtc = nowUtc,
        };
        review.SetTenant(tenantId);
        return Result.Success(review);
    }
}
