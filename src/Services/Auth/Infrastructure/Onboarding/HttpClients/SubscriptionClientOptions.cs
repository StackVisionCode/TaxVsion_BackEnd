namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>PayFlow (Fase 15) — base URL del servicio Subscription, usado por
/// <see cref="SubscriptionActivationClient"/>.</summary>
public sealed class SubscriptionClientOptions
{
    public const string SectionName = "Auth:Subscription";

    public string BaseUrl { get; set; } = "http://localhost:5360";
}
