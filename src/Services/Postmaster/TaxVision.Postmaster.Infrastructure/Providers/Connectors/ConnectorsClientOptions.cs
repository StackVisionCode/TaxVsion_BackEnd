namespace TaxVision.Postmaster.Infrastructure.Providers.Connectors;

/// <summary>Base URL del microservicio Connectors (D3 §4.4) — mismo patrón que <c>CloudStorageClientOptions</c>.</summary>
public sealed class ConnectorsClientOptions
{
    public const string SectionName = "Postmaster:Connectors";

    public string BaseUrl { get; set; } = "http://localhost:5390";
}
