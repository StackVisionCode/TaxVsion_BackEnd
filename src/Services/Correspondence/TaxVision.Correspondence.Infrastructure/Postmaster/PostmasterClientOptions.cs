namespace TaxVision.Correspondence.Infrastructure.Postmaster;

/// <summary>Base URL de Postmaster.Api (M2M ServiceOnly) — mismo patrón que <c>ConnectorsClientOptions</c>/<c>CloudStorageClientOptions</c>.</summary>
public sealed class PostmasterClientOptions
{
    public const string SectionName = "Correspondence:Postmaster";

    public string BaseUrl { get; set; } = "http://localhost:5370";
}
