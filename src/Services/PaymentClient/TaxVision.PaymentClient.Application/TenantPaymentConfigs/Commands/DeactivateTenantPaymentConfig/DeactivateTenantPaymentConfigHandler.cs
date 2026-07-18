using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.DeactivateTenantPaymentConfig;

public static class DeactivateTenantPaymentConfigHandler
{
    public static async Task<Result> Handle(
        DeactivateTenantPaymentConfigCommand command,
        ITenantPaymentConfigRepository configs,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var config = await configs.GetByTenantAndProviderAsync(command.TenantId, command.ProviderCode, ct);
        if (config is null)
            return Result.Failure(new Error("TenantPaymentConfig.NotFound", "TenantPaymentConfig does not exist."));

        var nowUtc = DateTime.UtcNow;
        var deactivateResult = config.Deactivate(command.Reason, command.ActorUserId, nowUtc);
        if (deactivateResult.IsFailure)
            return deactivateResult;

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(TenantPaymentConfig),
            config.Id,
            PaymentAuditAction.TenantPaymentConfigDeactivated,
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: (object?)null,
            reason: command.Reason,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
