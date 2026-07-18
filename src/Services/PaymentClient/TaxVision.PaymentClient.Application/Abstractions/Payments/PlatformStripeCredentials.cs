namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>
/// Credenciales de PLATAFORMA para operaciones de Stripe Connect (crear Connected Accounts,
/// generar onboarding links, cobrar direct charges con <c>Stripe-Account</c> header) — a
/// diferencia de <see cref="TenantProviderCredentials"/> (por tenant, modo DirectApiKeys),
/// esto es una sola cuenta Stripe, la de TaxVision. Vive en Application (no en Infrastructure)
/// porque tanto el gateway (Infrastructure) como los charge handlers (Application) necesitan
/// el mismo contrato. En producción se inyecta por variable de entorno
/// (<c>Stripe__PlatformSecretKey</c>, <c>Stripe__ConnectWebhookSecret</c>), nunca en
/// <c>appsettings.json</c>.
/// </summary>
public sealed class PlatformStripeCredentials
{
    public const string SectionName = "Stripe";

    public required string PlatformSecretKey { get; init; }
    public required string ConnectWebhookSecret { get; init; }
}
