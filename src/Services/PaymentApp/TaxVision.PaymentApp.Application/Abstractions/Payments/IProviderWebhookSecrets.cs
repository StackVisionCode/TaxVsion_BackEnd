using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions.Payments;

/// <summary>
/// Resuelve el webhook secret configurado globalmente para un provider (Fase A/B:
/// plataforma-global, no per-tenant — eso es PaymentClient/Fase E). Existe para que
/// Application pueda pedir "el secret de Stripe" sin importar <c>Infrastructure.Providers.Stripe</c>.
/// </summary>
public interface IProviderWebhookSecrets
{
    string? GetWebhookSecret(PaymentProviderCode code);
}
