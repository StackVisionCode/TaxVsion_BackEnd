using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Recurring;

namespace TaxVision.PaymentClient.Application.Recurring.Commands.CancelTenantRecurringPayment;

public static class CancelTenantRecurringPaymentHandler
{
    public static async Task<Result> Handle(
        CancelTenantRecurringPaymentCommand command,
        ITenantRecurringPaymentRepository plans,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var plan = await plans.GetByIdAsync(command.TenantRecurringPaymentId, command.TenantId, ct);
        if (plan is null)
            return Result.Failure(new Error("TenantRecurringPayment.NotFound", "TenantRecurringPayment does not exist."));

        var nowUtc = DateTime.UtcNow;
        var result = plan.Cancel(command.Reason, command.ActorUserId, nowUtc);
        if (result.IsFailure)
            return result;

        await AuditEntryFactory.AppendAsync(
            audit, command.TenantId, nameof(TenantRecurringPayment), plan.Id, PaymentAuditAction.TenantRecurringPaymentCancelled,
            command.ActorUserId, correlation.CorrelationId,
            before: (object?)null, after: (object?)null, reason: command.Reason, nowUtc, ct);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
