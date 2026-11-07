using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Reservations;

/// <summary>
/// Pago exitoso que llegó cuando la reserva ya estaba vencida y no se pudo verificar contra Payment.
/// Queda pendiente hasta que PlatformAdmin lo resuelva (dar por bueno el canje con el pago confirmado).
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

    [UserReference(UserReferenceKind.Provenance, Note = "PlatformAdmin que resolvió la revisión: historia.")]
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }

    /// <summary>El canje se dio por bueno con el pago confirmado por su dueño.</summary>
    public const string ResolutionCommitted = "Committed";

    public const int MaxNoteLength = 500;

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

    /// <summary>Cierra la revisión con quién la resolvió y por qué. Una sola vez.</summary>
    public Result Resolve(string resolution, Guid actorUserId, string? note, DateTime nowUtc)
    {
        if (ResolvedAtUtc is not null)
            return Result.Failure(
                new Error("Codes.LateCommitReview.AlreadyResolved", "This late commit review is already resolved.")
            );

        if (resolution != ResolutionCommitted)
            return Result.Failure(new Error("Codes.LateCommitReview.InvalidResolution", "Unknown resolution."));

        if (actorUserId == Guid.Empty)
            return Result.Failure(new Error("Codes.LateCommitReview.ActorRequired", "An actor is required."));

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxNoteLength })
            return Result.Failure(
                new Error("Codes.LateCommitReview.NoteTooLong", $"Note must be {MaxNoteLength} characters or fewer.")
            );

        Resolution = resolution;
        ResolvedByUserId = actorUserId;
        ResolutionNote = trimmed;
        ResolvedAtUtc = nowUtc;
        return Result.Success();
    }
}
