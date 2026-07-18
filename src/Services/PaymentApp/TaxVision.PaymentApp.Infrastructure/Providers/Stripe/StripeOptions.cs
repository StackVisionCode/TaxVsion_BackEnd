namespace TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

/// <summary>
/// Config mínima productiva de Stripe. En producción <see cref="SecretKey"/> y
/// <see cref="WebhookSecret"/> se inyectan por variable de entorno
/// (<c>Stripe__SecretKey</c>, <c>Stripe__WebhookSecret</c>), nunca en <c>appsettings.json</c>.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public required string SecretKey { get; init; }
    public required string WebhookSecret { get; init; }
}
