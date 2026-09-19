using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Application.Scheduling;

/// <summary>DTO de salida de un agendado — nunca se devuelve el aggregate al Api.</summary>
public sealed record CampaignScheduleResponse(
    Guid Id,
    Guid TenantId,
    Guid CampaignId,
    string Kind,
    string Status,
    DateTime? NextFireAtUtc,
    int? IntervalMinutes,
    IReadOnlyList<Guid> ContactListIds,
    bool IncludeCustomers,
    DateTime? LastFiredAtUtc,
    Guid? ActiveRunId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
)
{
    public static CampaignScheduleResponse From(CampaignSchedule s) =>
        new(
            s.Id,
            s.TenantId,
            s.CampaignId,
            s.Kind.ToString(),
            s.Status.ToString(),
            s.NextFireAtUtc,
            s.IntervalMinutes,
            s.ContactListIds(),
            s.IncludeCustomers,
            s.LastFiredAtUtc,
            s.ActiveRunId,
            s.CreatedAtUtc,
            s.UpdatedAtUtc
        );
}
