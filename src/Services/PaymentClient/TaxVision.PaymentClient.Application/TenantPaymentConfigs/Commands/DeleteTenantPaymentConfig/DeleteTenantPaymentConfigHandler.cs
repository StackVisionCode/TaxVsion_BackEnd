using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.DeleteTenantPaymentConfig;

/// <summary>Borra el config. Si el backend había creado el webhook en el proveedor, lo borra antes
/// (best-effort, con las claves que aún tenemos): un fallo solo se registra y el borrado sigue.</summary>
public static class DeleteTenantPaymentConfigHandler
{
    public const string HasPaymentsCode = "TenantPaymentConfig.HasPayments";

    public static async Task<Result> Handle(
        DeleteTenantPaymentConfigCommand command,
        ITenantPaymentConfigRepository configs,
        ITenantPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IPaymentAdapterFactory adapters,
        ISecretProtector secretProtector,
        ILogger<TenantPaymentConfig> logger,
        CancellationToken ct
    )
    {
        var config = await configs.GetByTenantAndProviderAsync(command.TenantId, command.ProviderCode, ct);
        if (config is null)
            return Result.Success(); // Idempotente: ya no existe.

        if (await payments.AnyForProviderAsync(command.TenantId, command.ProviderCode, ct))
            return Result.Failure(new Error(HasPaymentsCode, "This provider has payments. Turn it off instead."));

        if (config.ProviderWebhookId is { } automaticWebhookId)
            await PaymentWebhookAutoSetup.DeleteBestEffortAsync(
                config,
                automaticWebhookId,
                adapters,
                secretProtector,
                logger,
                ct
            );

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
            before: new
            {
                config.ProviderCode,
                config.Mode,
                config.IsActive,
            },
            after: (object?)null,
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
