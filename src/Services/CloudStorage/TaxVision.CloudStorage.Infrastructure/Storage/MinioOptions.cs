namespace TaxVision.CloudStorage.Infrastructure.Storage;

public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    /// <summary>Endpoint interno (red de Docker / loopback) — usado para TODAS las operaciones
    /// reales: bootstrap de buckets, upload/download/copy/delete server-side, multipart
    /// initiate/complete/abort. Nunca depende de DNS público ni de Caddy/TLS.</summary>
    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseTls { get; set; }

    /// <summary>Endpoint público — usado ÚNICAMENTE para generar URLs presignadas (upload
    /// policy, GET, partes de multipart) que el navegador del cliente debe poder alcanzar
    /// directo, sin pasar por esta API. Null/vacío => cae a <see cref="Endpoint"/> (comportamiento
    /// de antes, correcto para dev local donde ambos son el mismo host).</summary>
    public string? PublicEndpoint { get; set; }
    public bool? PublicUseTls { get; set; }

    public string EffectivePublicEndpoint => string.IsNullOrWhiteSpace(PublicEndpoint) ? Endpoint : PublicEndpoint;
    public bool EffectivePublicUseTls => PublicUseTls ?? UseTls;
}
