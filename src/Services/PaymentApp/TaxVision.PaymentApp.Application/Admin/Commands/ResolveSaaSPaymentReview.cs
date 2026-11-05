using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.Admin.Commands;

/// <summary>Registra la decisión que el admin tomó a mano sobre un pago en revisión. Nunca reembolsa ni
/// aplica beneficios: eso se hace en el panel del provider / <c>payments/{id}/refund</c> y en Subscription.</summary>
public sealed record ResolveSaaSPaymentReviewCommand(
    Guid SaaSPaymentId,
    string Resolution,
    string? Note,
    Guid ActorUserId
);

public static class ResolveSaaSPaymentReviewHandler
{
    public static async Task<Result> Handle(
        ResolveSaaSPaymentReviewCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        // Sin tenant: la bandeja es cross-tenant y el pago de onboarding puede no tenerlo.
        var payment = await payments.GetByIdAsync(command.SaaSPaymentId, ct);
        if (payment is null)
            return Result.Failure(new Error("SaaSPayment.NotFound", "SaaSPayment does not exist."));

        var nowUtc = DateTime.UtcNow;
        var resolved = payment.ResolveReview(command.Resolution, command.ActorUserId, nowUtc);
        if (resolved.IsFailure)
            return resolved;

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(SaaSPayment),
            payment.Id,
            PaymentAuditAction.SaaSPaymentReviewResolved,
            command.ActorUserId,
            correlation.CorrelationId,
            before: new { payment.ReviewReasonCode, payment.ReviewRequiredAtUtc },
            after: new { payment.ReviewResolution, payment.ReviewResolvedAtUtc },
            reason: command.Note,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
