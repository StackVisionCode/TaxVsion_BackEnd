namespace TaxVision.Connectors.Infrastructure.Providers.Watch;

/// <summary>Topic de Pub/Sub compartido por TaxVision (uno para todos los tenants), ej. "projects/taxvision/topics/gmail-push".</summary>
public sealed class GmailWatchOptions
{
    public const string SectionName = "Connectors:Watch:Gmail";

    public string TopicName { get; set; } = string.Empty;
}
