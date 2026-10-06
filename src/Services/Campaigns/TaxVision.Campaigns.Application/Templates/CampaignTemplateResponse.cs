using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Templates;

namespace TaxVision.Campaigns.Application.Templates;

/// <summary>Contenido por canal de una plantilla expuesto al Api.</summary>
public sealed record CampaignTemplateContentResponse(string Channel, string? Subject, string? Title, string Body);

/// <summary>DTO de salida de una plantilla de campaña — nunca se devuelve el aggregate al Api.</summary>
public sealed record CampaignTemplateResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    Guid CreatedByUserId,
    IReadOnlyList<string> Channels,
    IReadOnlyList<CampaignTemplateContentResponse> Contents,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
)
{
    public static CampaignTemplateResponse From(CampaignTemplate t) =>
        new(
            t.Id,
            t.TenantId,
            t.Name,
            t.Description,
            t.CreatedByUserId,
            SplitChannels(t.Channels),
            t.Contents.Select(c => new CampaignTemplateContentResponse(
                    c.Channel.ToString(),
                    c.Subject,
                    c.Title,
                    c.Body
                ))
                .ToList(),
            t.CreatedAtUtc,
            t.UpdatedAtUtc
        );

    private static IReadOnlyList<string> SplitChannels(CampaignChannel channels) =>
        Enum.GetValues<CampaignChannel>()
            .Where(c => c != CampaignChannel.None && channels.HasFlag(c))
            .Select(c => c.ToString())
            .ToList();
}
