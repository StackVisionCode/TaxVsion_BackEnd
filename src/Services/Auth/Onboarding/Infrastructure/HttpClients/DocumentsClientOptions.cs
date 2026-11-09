namespace TaxVision.Auth.Onboarding.Infrastructure.HttpClients;

public sealed class DocumentsClientOptions
{
    public const string SectionName = "Auth:Documents";

    public string BaseUrl { get; set; } = "http://localhost:5450";
}
