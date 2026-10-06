using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Domain.Templates;

/// <summary>Contenido de una plantilla para UN canal (misma forma que <c>CampaignContent</c>): Email usa
/// Subject+Body, Push Title+Body, SMS/WhatsApp Body. Entidad hija de <see cref="CampaignTemplate"/>.</summary>
public sealed class CampaignTemplateContent
{
    public Guid Id { get; }
    public Guid TemplateId { get; }
    public Guid TenantId { get; }
    public CampaignChannel Channel { get; private set; }
    public string? Subject { get; private set; }
    public string? Title { get; private set; }
    public string Body { get; private set; } = default!;

    private CampaignTemplateContent() { }

    internal CampaignTemplateContent(
        Guid templateId,
        Guid tenantId,
        CampaignChannel channel,
        string? subject,
        string? title,
        string body
    )
    {
        Id = Guid.NewGuid();
        TemplateId = templateId;
        TenantId = tenantId;
        Channel = channel;
        Subject = subject;
        Title = title;
        Body = body;
    }

    internal void Update(string? subject, string? title, string body)
    {
        Subject = subject;
        Title = title;
        Body = body;
    }
}
