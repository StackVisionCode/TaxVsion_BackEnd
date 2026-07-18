namespace TaxVision.Correspondence.Infrastructure.Connectors;

/// <summary>Base URL del microservicio Connectors (M2M ServiceOnly).</summary>
public sealed class ConnectorsClientOptions
{
    public const string SectionName = "Correspondence:Connectors";

    public string BaseUrl { get; set; } = "http://localhost:5390";
}
