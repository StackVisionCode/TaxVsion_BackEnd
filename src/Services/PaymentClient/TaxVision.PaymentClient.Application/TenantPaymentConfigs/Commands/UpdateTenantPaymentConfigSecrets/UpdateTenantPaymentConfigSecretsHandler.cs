using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.UpdateTenantPaymentConfigSecrets;

/// <summary>
/// Cifra y guarda las credenciales del provider para un tenant, y activa el config
/// automáticamente en el mismo paso si ambos secretos quedaron cargados — un config con
/// secretos recién cargados que siga inactivo no tiene ningún propósito.
/// </summary>
public static class UpdateTenantPaymentConfigSecretsHandler
{
    public static async Task<Result> Handle(
        UpdateTenantPaymentConfigSecretsCommand command,
        ITenantPaymentConfigRepository configs,
        ISecretProtector secretProtector,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var config = await configs.GetByTenantAndProviderAsync(command.TenantId, command.ProviderCode, ct);
        if (config is null)
            return Result.Failure(new Error("TenantPaymentConfig.NotFound", "TenantPaymentConfig does not exist."));

        var secretKeyResult = EncryptedSecret.Create(secretProtector.Protect(command.SecretKey));
        if (secretKeyResult.IsFailure)
            return Result.Failure(secretKeyResult.Error);

        var webhookSecretResult = EncryptedSecret.Create(secretProtector.Protect(command.WebhookSecret));
        if (webhookSecretResult.IsFailure)
            return Result.Failure(webhookSecretResult.Error);

        var nowUtc = DateTime.UtcNow;
        var updateResult = config.UpdateSecrets(secretKeyResult.Value, webhookSecretResult.Value, command.ActorUserId, nowUtc);
        if (updateResult.IsFailure)
            return updateResult;

        await AuditEntryFactory.AppendAsync(
            audit, command.TenantId, nameof(TenantPaymentConfig), config.Id, PaymentAuditAction.TenantPaymentConfigSecretsUpdated,
            command.ActorUserId, correlation.CorrelationId,
            before: (object?)null,
            after: (object?)null,
            reason: null, nowUtc, ct);

        var activateResult = config.MarkActive(command.ActorUserId, nowUtc);
        if (activateResult.IsSuccess)
        {
            await AuditEntryFactory.AppendAsync(
                audit, command.TenantId, nameof(TenantPaymentConfig), config.Id, PaymentAuditAction.TenantPaymentConfigActivated,
                command.ActorUserId, correlation.CorrelationId,
                before: (object?)null,
                after: (object?)null,
                reason: null, nowUtc, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
