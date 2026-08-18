namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>Gift/Referral en onboarding — base URL del servicio Growth (endpoints M2M
/// <c>internal/codes/*</c> e <c>internal/referrals/*</c>), usado por <c>GrowthOnboardingClient</c>.
/// Config <c>Auth:Growth:BaseUrl</c> (env por servicio; NO va por el gateway).</summary>
public sealed class GrowthClientOptions
{
    public const string SectionName = "Auth:Growth";

    public string BaseUrl { get; set; } = "http://localhost:5300";
}
