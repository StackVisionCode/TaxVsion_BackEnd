using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Recurring.Commands.CreateTenantRecurringPayment;

public static class CreateTenantRecurringPaymentHandler
{
    public static async Task<Result<Guid>> Handle(
        CreateTenantRecurringPaymentCommand command,
        ITenantRecurringPaymentRepository plans,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var amountResult = Money.Create(command.AmountCents, command.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<Guid>(amountResult.Error);

        var purposeResult = PaymentPurpose.Create(command.PurposeKind, command.PurposeExternalReferenceId);
        if (purposeResult.IsFailure)
            return Result.Failure<Guid>(purposeResult.Error);

        var nowUtc = DateTime.UtcNow;
        var createResult = TenantRecurringPayment.Create(
            command.TenantId,
            command.TaxpayerId,
            command.ProviderCode,
            command.PaymentMethodReference,
            amountResult.Value,
            purposeResult.Value,
            command.BillingCycle,
            command.CustomIntervalDays,
            command.StartDate,
            command.EndDate,
            command.MaxExecutions,
            RetryPolicy.Default,
            command.PlatformFeeAmountCents,
            command.PlatformFeeReference,
            command.ActorUserId,
            nowUtc
        );
        if (createResult.IsFailure)
            return Result.Failure<Guid>(createResult.Error);

        var plan = createResult.Value;
        var generateResult = plan.GenerateSchedules(1, nowUtc);
        if (generateResult.IsFailure)
            return Result.Failure<Guid>(generateResult.Error);

        await plans.AddAsync(plan, ct);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(TenantRecurringPayment),
            plan.Id,
            PaymentAuditAction.TenantRecurringPaymentCreated,
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                plan.Amount.AmountCents,
                plan.Amount.Currency,
                plan.BillingCycle,
                plan.StartDate,
            },
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(plan.Id);
    }
}
