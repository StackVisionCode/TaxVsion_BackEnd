using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Common;

/// <summary>
/// Reglas del registro automático del webhook, compartidas por guardar claves, activar y el catálogo.
/// Se registra solo si la función está encendida, el adaptador lo soporta (capacidad + ruta + eventos),
/// el config es DirectApiKeys, la oficina no pegó un secreto manual y: no hay secreto guardado, o el que
/// hay lo creó el backend y cambió la clave secreta (puede ser otra cuenta del proveedor).
/// </summary>
public static class PaymentWebhookAutoSetup
{
    /// <summary>El adaptador sabe registrar y el descriptor dice dónde y qué eventos.</summary>
    public static bool IsSupported(IPaymentAdapterFactory adapters, PaymentProviderCode code) =>
        FindRegistrar(adapters, code) is not null;

    public static IPaymentWebhookRegistration? FindRegistrar(
        IPaymentAdapterFactory adapters,
        PaymentProviderCode code
    ) =>
        adapters.FindDescriptor(code) is { WebhookRouteName: not null, WebhookEvents.Count: > 0 }
        && adapters.Resolve(code) is IPaymentWebhookRegistration registrar
            ? registrar
            : null;

    public static bool ShouldRegister(
        TenantPaymentConfig config,
        PaymentWebhookOptions options,
        IPaymentAdapterFactory adapters,
        bool secretKeyChanged
    ) =>
        options.IsAutoSetupEnabled
        && config.Mode == TenantPaymentMode.DirectApiKeys
        && config.SecretKeyEncrypted is not null
        && config.WebhookSetup is not (WebhookSetupStatus.Manual or WebhookSetupStatus.Pending)
        && (
            config.WebhookSecretEncrypted is null
            || (config.WebhookSetup == WebhookSetupStatus.Automatic && secretKeyChanged)
        )
        && IsSupported(adapters, config.ProviderCode);

    /// <summary>Borra un webhook creado por el backend; un fallo solo se registra.</summary>
    public static async Task<bool> DeleteBestEffortAsync(
        TenantPaymentConfig config,
        string providerWebhookId,
        IPaymentAdapterFactory adapters,
        ISecretProtector secretProtector,
        ILogger logger,
        CancellationToken ct
    )
    {
        var registrar = FindRegistrar(adapters, config.ProviderCode);
        if (registrar is null || Credentials(config, secretProtector) is not { } credentials)
            return false;

        try
        {
            var deleted = await registrar.DeleteWebhookAsync(credentials, providerWebhookId, ct);
            if (deleted.IsSuccess)
                return true;

            logger.LogWarning(
                "Could not delete the automatic webhook of tenant {TenantId} {Provider}: {Code}.",
                config.TenantId,
                config.ProviderCode,
                deleted.Error.Code
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Deleting the automatic webhook of tenant {TenantId} {Provider} threw.",
                config.TenantId,
                config.ProviderCode
            );
        }

        return false;
    }

    public static TenantProviderCredentials? Credentials(
        TenantPaymentConfig config,
        ISecretProtector secretProtector
    ) =>
        config.SecretKeyEncrypted is not null
        && secretProtector.TryUnprotect(config.SecretKeyEncrypted.CipherText, out var secretKey, out _)
            ? new TenantProviderCredentials(secretKey, null, config.PublishableKey, config.ApiBaseUrl)
            : null;
}
