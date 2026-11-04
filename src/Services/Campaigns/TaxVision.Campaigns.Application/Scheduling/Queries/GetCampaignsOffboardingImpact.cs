using TaxVision.Campaigns.Application.Scheduling.Abstractions;

namespace TaxVision.Campaigns.Application.Scheduling.Queries;

/// <summary>Pre-flight del retiro: cuántos agendados vivos disparan en nombre de este empleado.</summary>
public sealed record GetCampaignsOffboardingImpactQuery(Guid TenantId, Guid UserId);

/// <summary>
/// <paramref name="ActiveSchedules"/>: agendados activos o pausados que, al retirarlo, pasan al sucesor (o
/// se pausan si no hay sucesor elegible).
/// </summary>
public sealed record CampaignsOffboardingImpactResponse(int ActiveSchedules);

public static class GetCampaignsOffboardingImpactHandler
{
    public static async Task<CampaignsOffboardingImpactResponse> Handle(
        GetCampaignsOffboardingImpactQuery query,
        ICampaignScheduleRepository schedules,
        CancellationToken ct
    ) =>
        new(query.UserId == Guid.Empty ? 0 : await schedules.CountOpenRunAsUserAsync(query.TenantId, query.UserId, ct));
}
