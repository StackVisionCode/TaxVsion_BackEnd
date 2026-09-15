using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.DeleteTenantPaymentConfig;

public static class DeleteTenantPaymentConfigHandler
{
    public static async Task<Result> Handle(
        DeleteTenantPaymentConfigCommand command,
        ITenantPaymentConfigRepository configs,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var config = await configs.GetByTenantAndProviderAsync(command.TenantId, command.ProviderCode, ct);
        if (config is null)
            return Result.Success(); // Idempotente: ya no existe.

        var nowUtc = DateTime.UtcNow;
        configs.Remove(config);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(TenantPaymentConfig),
            config.Id,
            PaymentAuditAction.TenantPaymentConfigDeleted,
            command.ActorUserId,
            correlation.CorrelationId,
            before: new { config.ProviderCode, config.Mode, config.IsActive },
            after: (object?)null,
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
