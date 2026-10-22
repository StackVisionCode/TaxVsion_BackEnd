namespace TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;

/// <summary>
/// Consulta de respaldo a SMTP2GO (<c>/v3/activity/search</c>). Sin <see cref="ApiKey"/> la consulta queda
/// apagada y el webhook sigue funcionando solo. La key necesita solo permiso de lectura de actividad.
/// </summary>
public sealed class Smtp2GoFeedbackOptions
{
    public const string SectionName = "DeliveryFeedback:Smtp2Go";

    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.smtp2go.com/v3/";

    /// <summary>Pausa entre consultas: la API de SMTP2GO limita a 60 por minuto.</summary>
    public int DelayBetweenRequestsMs { get; set; } = 1100;
}
