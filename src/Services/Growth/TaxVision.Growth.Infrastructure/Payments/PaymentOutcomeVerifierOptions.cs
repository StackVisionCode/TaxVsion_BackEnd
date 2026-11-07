namespace TaxVision.Growth.Infrastructure.Payments;

public sealed class PaymentOutcomeVerifierOptions
{
    public const string SectionName = "Growth:PaymentOutcomeVerifier";

    /// <summary>Apagado: ningún commit tardío se da por bueno solo, queda en revisión. Encendido en appsettings.json.</summary>
    public bool Enabled { get; init; }
    public string? PaymentAppBaseUrl { get; init; }
    public string? PaymentClientBaseUrl { get; init; }

    /// <summary>Tiempo máximo de la consulta a PaymentApp; pasado, el commit se reintenta.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}
