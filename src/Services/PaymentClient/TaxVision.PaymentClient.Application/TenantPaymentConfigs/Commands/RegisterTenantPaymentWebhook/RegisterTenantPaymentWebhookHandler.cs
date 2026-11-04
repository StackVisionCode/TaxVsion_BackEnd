using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Common;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.RegisterTenantPaymentWebhook;

/// <summary>
/// Crea el webhook de la oficina en el proveedor con sus mismas claves y guarda solo el secreto (cifrado)
/// y el id del webhook. Opcional y fuera del guardado: un fallo pasajero se reprograma hasta
/// <see cref="PaymentWebhookOptions.MaxAttempts"/>; uno definitivo (clave sin permiso, URL rechazada) o el
/// último intento deja el config en <see cref="WebhookSetupStatus.Failed"/>, y la oficina sigue como antes.
/// Idempotente: si el config ya no está pendiente (llegó un secreto manual, se borró) no hace nada.
/// </summary>
public static class RegisterTenantPaymentWebhookHandler
{
    public static async Task Handle(
        RegisterTenantPaymentWebhookCommand command,
        ITenantPaymentConfigRepository configs,
        IPaymentAdapterFactory adapters,
        ISecretProtector secretProtector,
        IPaymentWebhookUrlBuilder urlBuilder,
        IOptions<PaymentWebhookOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<TenantPaymentConfig> logger,
        CancellationToken ct
    )
    {
        var config = await configs.GetByTenantAndProviderAsync(command.TenantId, command.ProviderCode, ct);
        if (config is null || config.WebhookSetup != WebhookSetupStatus.Pending)
            return;

        var settings = options.Value;
        var outcome = await RegisterAsync(config, settings, adapters, secretProtector, urlBuilder, ct);
        if (outcome.IsSuccess)
        {
            await CompleteAsync(config, outcome.Value, adapters, secretProtector, unitOfWork, logger, ct);
            return;
        }

        var error = outcome.Error;
        if (error.Code == WebhookRegistrationErrors.TransientCode && command.Attempt < settings.MaxAttempts)
        {
            logger.LogWarning(
                "Webhook auto-setup for tenant {TenantId} {Provider} failed (attempt {Attempt}/{Max}): {Code}. Retrying.",
                command.TenantId,
                command.ProviderCode,
                command.Attempt,
                settings.MaxAttempts,
                error.Code
            );
            await bus.ScheduleAsync(
                command with
                {
                    Attempt = command.Attempt + 1,
                },
                TimeSpan.FromSeconds(settings.RetryDelaySeconds)
            );
            return;
        }

        config.FailAutomaticWebhookSetup(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning(
            "Webhook auto-setup for tenant {TenantId} {Provider} failed after {Attempt} attempt(s): {Code}. The office can still configure it manually.",
            command.TenantId,
            command.ProviderCode,
            command.Attempt,
            error.Code
        );
    }

    private static async Task<Result<RegisteredWebhook>> RegisterAsync(
        TenantPaymentConfig config,
        PaymentWebhookOptions settings,
        IPaymentAdapterFactory adapters,
        ISecretProtector secretProtector,
        IPaymentWebhookUrlBuilder urlBuilder,
        CancellationToken ct
    )
    {
        if (!settings.IsAutoSetupEnabled || config.Mode != TenantPaymentMode.DirectApiKeys)
            return Fail("Webhook.AutoSetupDisabled", "Automatic webhook setup is turned off.");

        var registrar = PaymentWebhookAutoSetup.FindRegistrar(adapters, config.ProviderCode);
        var descriptor = adapters.FindDescriptor(config.ProviderCode);
        if (registrar is null || descriptor?.WebhookRouteName is not { } routeName)
            return Fail("Webhook.AutoSetupNotSupported", "The provider does not support automatic webhook setup.");

        var url = urlBuilder.Build(routeName, config.TenantId);
        if (url is null)
            return Fail("Webhook.UrlUnavailable", "The public webhook URL could not be built.");

        if (PaymentWebhookAutoSetup.Credentials(config, secretProtector) is not { } credentials)
            return Fail("TenantPaymentConfig.SecretUnreadable", "Stored provider secret could not be decrypted.");

        return await registrar.RegisterWebhookAsync(
            credentials,
            new WebhookRegistrationRequest(url, descriptor.WebhookEvents ?? []),
            ct
        );
    }

    private static async Task CompleteAsync(
        TenantPaymentConfig config,
        RegisteredWebhook webhook,
        IPaymentAdapterFactory adapters,
        ISecretProtector secretProtector,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        var secret = EncryptedSecret.Create(secretProtector.Protect(webhook.Secret));
        var completed = secret.IsSuccess
            ? config.CompleteAutomaticWebhookSetup(secret.Value, webhook.ProviderWebhookId, DateTime.UtcNow)
            : Result.Failure(secret.Error);

        if (completed.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Webhook auto-setup for tenant {TenantId} {Provider} completed.",
                config.TenantId,
                config.ProviderCode
            );
            return;
        }

        // Mientras tanto ganó un secreto manual (o no se pudo cifrar): el webhook recién creado sobra.
        await PaymentWebhookAutoSetup.DeleteBestEffortAsync(
            config,
            webhook.ProviderWebhookId,
            adapters,
            secretProtector,
            logger,
            ct
        );
    }

    private static Result<RegisteredWebhook> Fail(string code, string message) =>
        Result.Failure<RegisteredWebhook>(new Error(code, message));
}
