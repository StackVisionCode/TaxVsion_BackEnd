namespace TaxVision.Auth.Application.Onboarding;

/// <summary>Sweeper que cierra onboardings zombies cuando el buyer abandona el checkout del provider
/// (apretó "atrás" en el navegador y la pestaña del landing quedó clavada en processing). Fail-closed:
/// sin flag, el job no corre. Sección <c>Onboarding:Expiration</c>.</summary>
public sealed class OnboardingExpirationOptions
{
    public const string SectionName = "Onboarding:Expiration";

    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 5;
    public int TtlMinutes { get; set; } = 30;
    public int BatchSize { get; set; } = 100;
}
