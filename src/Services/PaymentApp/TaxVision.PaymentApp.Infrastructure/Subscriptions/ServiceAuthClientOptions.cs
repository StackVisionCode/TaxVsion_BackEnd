namespace TaxVision.PaymentApp.Infrastructure.Subscriptions;

/// <summary>PayFlow (Fase 16) — primer cliente saliente de PaymentApp hacia otro microservicio de
/// TaxVision (hasta ahora solo llamaba proveedores externos: Stripe/Intellipay). Mismo patrón de
/// dos piezas que el resto del repo (ver Guia_Conectar_Microservicio_a_CloudStorage.md): este
/// options acredita contra <c>POST auth/service-token</c>, <see cref="SubscriptionClientOptions"/>
/// apunta al servicio real.</summary>
public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "ServiceAuthClient";

    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class SubscriptionClientOptions
{
    public const string SectionName = "SubscriptionClient";

    /// <summary>Base URL de Subscription. En Docker: http://subscription-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5360";
}
