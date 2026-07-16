using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Payouts;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Payouts.Commands.UpsertPayoutSchedule;

public static class UpsertPayoutScheduleHandler
{
    public static async Task<Result<Guid>> Handle(
        UpsertPayoutScheduleCommand command,
        ITenantConnectAccountRepository connectAccounts,
        IPayoutScheduleRepository schedules,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var connectAccount = await connectAccounts.GetByTenantAndProviderAsync(command.TenantId, PaymentProviderCode.Stripe, ct);
        if (connectAccount is null)
            return Result.Failure<Guid>(new Error("TenantConnectAccount.NotFound", "TenantConnectAccount does not exist."));

        var nowUtc = DateTime.UtcNow;
        var existing = await schedules.GetByTenantConnectAccountIdAsync(connectAccount.Id, ct);

        if (existing is not null)
        {
            var updateResult = existing.UpdateFrequency(command.Frequency, command.Anchor, command.ActorUserId, nowUtc);
            if (updateResult.IsFailure)
                return Result.Failure<Guid>(updateResult.Error);

            await AuditEntryFactory.AppendAsync(
                audit, command.TenantId, nameof(PayoutSchedule), existing.Id, PaymentAuditAction.PayoutScheduleUpdated,
                command.ActorUserId, correlation.CorrelationId,
                before: (object?)null,
                after: new { command.Frequency, command.Anchor },
                reason: null, nowUtc, ct);

            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(existing.Id);
        }

        var createResult = PayoutSchedule.Create(command.TenantId, connectAccount.Id, command.Frequency, command.Anchor, command.Currency, command.ActorUserId, nowUtc);
        if (createResult.IsFailure)
            return Result.Failure<Guid>(createResult.Error);

        var schedule = createResult.Value;
        await schedules.AddAsync(schedule, ct);

        await AuditEntryFactory.AppendAsync(
            audit, command.TenantId, nameof(PayoutSchedule), schedule.Id, PaymentAuditAction.PayoutScheduleCreated,
            command.ActorUserId, correlation.CorrelationId,
            before: (object?)null,
            after: new { command.Frequency, command.Anchor, command.Currency },
            reason: null, nowUtc, ct);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(schedule.Id);
    }
}
