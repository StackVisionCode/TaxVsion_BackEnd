using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.UpdateTenantPaymentConfigUrl;

public static class UpdateTenantPaymentConfigUrlHandler
{
    public static async Task<Result> Handle(
        UpdateTenantPaymentConfigUrlCommand command,
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
        var before = config.ApiBaseUrl;

        var updateResult = config.UpdateApiBaseUrl(command.ApiBaseUrl, command.ActorUserId, nowUtc);
        if (updateResult.IsFailure)
            return updateResult;

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(TenantPaymentConfig),
            config.Id,
            PaymentAuditAction.TenantPaymentConfigUpdated,
            command.ActorUserId,
            correlation.CorrelationId,
            before: new { ApiBaseUrl = before },
            after: new { config.ApiBaseUrl },
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
