namespace TaxVision.Campaigns.Application.Runs;

/// <summary>
/// Runs más recientes de la oficina para el dashboard, sin destinatarios (sin PII). <c>NextFireAtUtc</c>: agendado
/// Active más próximo de la oficina, desde ahora.
/// </summary>
public sealed record RecentCampaignRunsResponse(IReadOnlyList<RecentCampaignRunItem> Items, DateTime? NextFireAtUtc);

/// <summary>Un run con su campaña; <c>CampaignNextFireAtUtc</c> es el próximo disparo Active de esa campaña.</summary>
public sealed record RecentCampaignRunItem(
    Guid RunId,
    Guid CampaignId,
    string? CampaignName,
    string? CampaignStatus,
    string Status,
    string TriggerKind,
    int RecipientCount,
    int Dispatched,
    int Accepted,
    int Delivered,
    int Failed,
    int Skipped,
    int Unknown,
    DateTime CreatedAtUtc,
    DateTime? FinishedAtUtc,
    DateTime? CampaignNextFireAtUtc
);
