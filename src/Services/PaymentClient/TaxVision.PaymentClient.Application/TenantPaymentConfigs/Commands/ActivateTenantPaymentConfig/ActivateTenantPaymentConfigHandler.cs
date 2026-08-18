using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.ActivateTenantPaymentConfig;

public static class ActivateTenantPaymentConfigHandler
{
    public static async Task<Result> Handle(
        ActivateTenantPaymentConfigCommand command,
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
        var activateResult =
            config.Mode == TenantPaymentMode.Connect
                ? config.MarkActiveViaConnect(command.ActorUserId, nowUtc)
                : config.MarkActive(command.ActorUserId, nowUtc);
        if (activateResult.IsFailure)
            return activateResult;

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(TenantPaymentConfig),
            config.Id,
            PaymentAuditAction.TenantPaymentConfigActivated,
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: (object?)null,
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
