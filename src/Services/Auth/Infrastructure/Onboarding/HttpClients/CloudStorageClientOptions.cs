namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

public sealed class CloudStorageClientOptions
{
    public const string SectionName = "Auth:CloudStorage";

    public string BaseUrl { get; set; } = "http://localhost:5330";
}
