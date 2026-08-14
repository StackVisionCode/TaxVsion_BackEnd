namespace TaxVision.Tasks.Infrastructure.Jobs;

public sealed class StaleAttachmentOptions
{
    public const string SectionName = "Tasks:StaleAttachments";

    public int IntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Por debajo de esta antigüedad el escaneo puede seguir en curso y preguntar sería ruido. Se
    /// baja en entornos de prueba, donde el veredicto llega en segundos.
    /// </summary>
    public int GraceMinutes { get; set; } = 10;

    public int BatchSize { get; set; } = 100;
}
