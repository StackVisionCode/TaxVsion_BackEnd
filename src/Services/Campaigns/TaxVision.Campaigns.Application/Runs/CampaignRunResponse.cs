using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs;

public sealed record CampaignRecipientResponse(
    Guid Id,
    string ContactRef,
    string Channel,
    string? Email,
    string? PhoneE164,
    string State,
    string? Reason,
    string? ProviderRef,
    string DispatchId
)
{
    public static CampaignRecipientResponse From(CampaignRecipient r) =>
        new(r.Id, r.ContactRef, r.Channel.ToString(), r.Email, r.PhoneE164, r.State.ToString(), r.Reason, r.ProviderRef, r.DispatchId);
}

public sealed record CampaignRunResponse(
    Guid Id,
    Guid TenantId,
    Guid CampaignId,
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
    IReadOnlyList<CampaignRecipientResponse> Recipients
)
{
    public static CampaignRunResponse From(CampaignRun run) =>
        new(
            run.Id,
            run.TenantId,
            run.CampaignId,
            run.Status.ToString(),
            run.TriggerKind,
            run.RecipientCount,
            run.CounterDispatched,
            run.CounterAccepted,
            run.CounterDelivered,
            run.CounterFailed,
            run.CounterSkipped,
            run.CounterUnknown,
            run.CreatedAtUtc,
            run.FinishedAtUtc,
            run.Recipients.Select(CampaignRecipientResponse.From).ToList()
        );
}
