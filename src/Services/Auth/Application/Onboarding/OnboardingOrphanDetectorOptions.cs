namespace TaxVision.Auth.Application.Onboarding;

/// <summary>OBF-2.7 — sweeper que detecta onboardings huérfanos (cliente pagó pero nunca completó el
/// office-setup form) y publica un evento interno para alerta/metrica. Fail-closed: sin flag no corre.
/// Sección <c>Onboarding:OrphanDetector</c>.</summary>
public sealed class OnboardingOrphanDetectorOptions
{
    public const string SectionName = "Onboarding:OrphanDetector";

    public bool Enabled { get; set; } = true;

    /// <summary>Cadencia del sweeper. Default 60 min — más agresivo satura métricas si hay backlog.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Ventana desde PaymentCompletedAtUtc tras la cual un onboarding sin RegistrationCompleted
    /// se considera huérfano. Default 24h — da tiempo al cliente a leer email en zonas horarias distintas
    /// y evita falsos positivos por checkouts que literalmente ocurrieron minutos atrás.</summary>
    public int WindowHours { get; set; } = 24;

    public int BatchSize { get; set; } = 100;
}
