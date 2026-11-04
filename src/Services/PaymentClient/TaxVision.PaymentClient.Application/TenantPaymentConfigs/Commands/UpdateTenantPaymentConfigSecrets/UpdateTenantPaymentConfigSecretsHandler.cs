using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
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

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.UpdateTenantPaymentConfigSecrets;

/// <summary>
/// Cifra y guarda las credenciales del provider para un tenant, y activa el config
/// automáticamente en el mismo paso — un config con secretos recién cargados que siga inactivo no
/// tiene ningún propósito. El webhook secret es opcional: vacío conserva el que ya hubiera. Sin secreto
/// manual, encola el registro automático del webhook (<see cref="PaymentWebhookAutoSetup"/>); con uno manual,
/// borra (best-effort) el webhook que hubiera creado el backend. Nada de eso hace fallar el guardado.
/// </summary>
public static class UpdateTenantPaymentConfigSecretsHandler
{
    public static async Task<Result> Handle(
        UpdateTenantPaymentConfigSecretsCommand command,
        ITenantPaymentConfigRepository configs,
        IPaymentAdapterFactory adapters,
        ISecretProtector secretProtector,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IOptions<PaymentWebhookOptions> webhookOptions,
        IMessageBus bus,
        ILogger<TenantPaymentConfig> logger,
        CancellationToken ct
    )
    {
        var supported = adapters.RequireSupported(command.ProviderCode);
        if (supported.IsFailure)
            return Result.Failure(supported.Error);

        var config = await configs.GetByTenantAndProviderAsync(command.TenantId, command.ProviderCode, ct);
        if (config is null)
            return Result.Failure(new Error("TenantPaymentConfig.NotFound", "TenantPaymentConfig does not exist."));

        var secretKeyResult = EncryptedSecret.Create(secretProtector.Protect(command.SecretKey));
        if (secretKeyResult.IsFailure)
            return Result.Failure(secretKeyResult.Error);

        // Vacío = no enviado: el dominio conserva el que ya estuviera guardado.
        EncryptedSecret? webhookSecret = null;
        if (!string.IsNullOrWhiteSpace(command.WebhookSecret))
        {
            var webhookSecretResult = EncryptedSecret.Create(secretProtector.Protect(command.WebhookSecret));
            if (webhookSecretResult.IsFailure)
                return Result.Failure(webhookSecretResult.Error);
            webhookSecret = webhookSecretResult.Value;
        }

        // Cambiar la clave puede ser cambiar de cuenta: el webhook automático de la anterior no sirve.
        var secretKeyChanged = !(
            config.SecretKeyEncrypted is not null
            && secretProtector.TryUnprotect(config.SecretKeyEncrypted.CipherText, out var previousKey, out _)
            && string.Equals(previousKey, command.SecretKey, StringComparison.Ordinal)
        );

        var nowUtc = DateTime.UtcNow;
        var updateResult = config.UpdateSecrets(secretKeyResult.Value, webhookSecret, command.ActorUserId, nowUtc);
        if (updateResult.IsFailure)
            return updateResult;

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(TenantPaymentConfig),
            config.Id,
            PaymentAuditAction.TenantPaymentConfigSecretsUpdated,
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: (object?)null,
            reason: null,
            nowUtc,
            ct
        );

        var activateResult = config.MarkActive(command.ActorUserId, nowUtc);
        if (activateResult.IsSuccess)
        {
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
        }

        var registerWebhook =
            webhookSecret is null
            && PaymentWebhookAutoSetup.ShouldRegister(config, webhookOptions.Value, adapters, secretKeyChanged)
            && config.BeginAutomaticWebhookSetup(nowUtc).IsSuccess;

        // Guardar antes de publicar: el comando sale solo si las claves quedaron guardadas.
        await unitOfWork.SaveChangesAsync(ct);

        if (registerWebhook)
            await bus.PublishAsync(new RegisterTenantPaymentWebhookCommand(command.TenantId, command.ProviderCode));
        else if (webhookSecret is not null && config.ProviderWebhookId is { } automaticWebhookId)
            await RemoveAutomaticWebhookAsync(
                config,
                automaticWebhookId,
                adapters,
                secretProtector,
                unitOfWork,
                logger,
                ct
            );

        return Result.Success();
    }

    // Ganó el secreto manual: el webhook automático firmaría con otro secreto y fallaría la verificación.
    private static async Task RemoveAutomaticWebhookAsync(
        TenantPaymentConfig config,
        string automaticWebhookId,
        IPaymentAdapterFactory adapters,
        ISecretProtector secretProtector,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (
            !await PaymentWebhookAutoSetup.DeleteBestEffortAsync(
                config,
                automaticWebhookId,
                adapters,
                secretProtector,
                logger,
                ct
            )
        )
            return;

        config.ForgetProviderWebhook(automaticWebhookId);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
