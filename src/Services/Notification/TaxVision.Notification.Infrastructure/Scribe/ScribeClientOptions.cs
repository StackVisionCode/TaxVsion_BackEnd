namespace TaxVision.Notification.Infrastructure.Scribe;

public sealed class ScribeClientOptions
{
    public const string SectionName = "ScribeClient";

    /// <summary>Base URL del microservicio Scribe (directo o vía Gateway). En Docker: http://scribe-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5380";
}
