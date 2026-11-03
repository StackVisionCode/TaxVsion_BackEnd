using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>
/// Capacidad opcional de un adaptador: crear y borrar el webhook de la oficina en el proveedor con sus
/// propias credenciales, para que no tenga que configurarlo a mano. Un adaptador que no la implementa
/// simplemente no ofrece el registro automático.
/// </summary>
public interface IPaymentWebhookRegistration
{
    /// <summary>Crea (o reutiliza, si el proveedor no deja duplicar) el webhook para la URL y eventos.
    /// Un fallo pasajero (red, 5xx, 429) viene con <see cref="WebhookRegistrationErrors.TransientCode"/>.</summary>
    Task<Result<RegisteredWebhook>> RegisterWebhookAsync(
        TenantProviderCredentials credentials,
        WebhookRegistrationRequest request,
        CancellationToken ct
    );

    /// <summary>Borra un webhook que creó el backend. Uno que ya no existe cuenta como borrado.</summary>
    Task<Result> DeleteWebhookAsync(
        TenantProviderCredentials credentials,
        string providerWebhookId,
        CancellationToken ct
    );
}

public sealed record WebhookRegistrationRequest(Uri Url, IReadOnlyList<string> Events);

/// <summary><paramref name="Secret"/>: lo que se guarda como webhook secret (Stripe: signing secret;
/// PayPal: el propio Webhook ID que pide verify-webhook-signature).</summary>
public sealed record RegisteredWebhook(string ProviderWebhookId, string Secret);

public static class WebhookRegistrationErrors
{
    /// <summary>Fallo pasajero: vale la pena reintentar.</summary>
    public const string TransientCode = "Webhook.Registration.Transient";
}
