using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.RegisterTenantPaymentWebhook;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.ActivateTenantPaymentConfig;

/// <summary>Activa el config. En DirectApiKeys sin webhook (nunca intentado o fallido) vuelve a encolar el
/// registro automático: reactivar es otra oportunidad de dejarlo listo.</summary>
public static class ActivateTenantPaymentConfigHandler
{
    public static async Task<Result> Handle(
        ActivateTenantPaymentConfigCommand command,
        ITenantPaymentConfigRepository configs,
        IPaymentAdapterFactory adapters,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IOptions<PaymentWebhookOptions> webhookOptions,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var supported = adapters.RequireSupported(command.ProviderCode);
        if (supported.IsFailure)
            return Result.Failure(supported.Error);

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

        var registerWebhook =
            PaymentWebhookAutoSetup.ShouldRegister(config, webhookOptions.Value, adapters, secretKeyChanged: false)
            && config.BeginAutomaticWebhookSetup(nowUtc).IsSuccess;

        // Guardar antes de publicar.
        await unitOfWork.SaveChangesAsync(ct);
        if (registerWebhook)
            await bus.PublishAsync(new RegisterTenantPaymentWebhookCommand(command.TenantId, command.ProviderCode));

        return Result.Success();
    }
}
