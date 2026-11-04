namespace TaxVision.Auth.Application.Offboarding;

/// <summary>Seguimiento del retiro con acuse por servicio. Sección <c>Offboarding:Tracking</c>.</summary>
public sealed class OffboardingTrackingOptions
{
    public const string SectionName = "Offboarding:Tracking";

    /// <summary>Apagado = no corre el barrido (los acuses se siguen registrando).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Plazo desde el retiro tras el cual un paso sin acuse dispara el aviso.</summary>
    public int TimeoutMinutes { get; set; } = 30;

    /// <summary>Cadencia del barrido.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Tope de corridas por tick.</summary>
    public int BatchSize { get; set; } = 100;

    public TimeSpan Timeout => TimeSpan.FromMinutes(TimeoutMinutes);

    /// <summary>Errores de configuración (vacío = válida).</summary>
    public IEnumerable<string> Errors()
    {
        if (TimeoutMinutes is < 5 or > 10_080)
            yield return "Offboarding:Tracking:TimeoutMinutes must be between 5 and 10080.";
        if (IntervalMinutes is < 1 or > 1_440)
            yield return "Offboarding:Tracking:IntervalMinutes must be between 1 and 1440.";
        if (BatchSize is < 1 or > 1_000)
            yield return "Offboarding:Tracking:BatchSize must be between 1 and 1000.";
    }
}
