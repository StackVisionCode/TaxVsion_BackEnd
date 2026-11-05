using BuildingBlocks.Common;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Permissions.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Permissions;
using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Tests.Fakes;

/// <summary>Agendados y constancias en memoria, con la misma regla de "en nombre de quién dispara" que el repo real.</summary>
internal sealed class FakeCampaignScheduleRepository : ICampaignScheduleRepository
{
    public List<CampaignSchedule> Schedules { get; } = [];
    public List<CampaignScheduleHandover> Handovers { get; } = [];

    public Task<CampaignSchedule?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Schedules.Find(s => s.TenantId == tenantId && s.Id == id));

    public Task<PagedResult<CampaignSchedule>> ListByCampaignAsync(
        Guid tenantId,
        Guid campaignId,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    /// <summary>Simula que la búsqueda por ActiveRunId no encuentra el agendado.</summary>
    public bool ActiveRunLookupMisses { get; set; }

    public Task<CampaignSchedule?> GetByActiveRunIdAsync(Guid tenantId, Guid runId, CancellationToken ct = default) =>
        Task.FromResult(
            ActiveRunLookupMisses ? null : Schedules.Find(s => s.TenantId == tenantId && s.ActiveRunId == runId)
        );

    public Task<IReadOnlyList<ClaimedSchedule>> ClaimDueAsync(
        DateTime nowUtc,
        int leaseSeconds,
        int batchSize,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task AddAsync(CampaignSchedule schedule, CancellationToken ct = default)
    {
        Schedules.Add(schedule);
        return Task.CompletedTask;
    }

    public Task<StuckScheduleRelease> ReleaseStuckAsync(
        DateTime nowUtc,
        TimeSpan grace,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<CampaignSchedule>> ListOpenRunAsUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<CampaignSchedule>>(OpenRunAs(tenantId, userId).ToList());

    public Task<int> CountOpenRunAsUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(OpenRunAs(tenantId, userId).Count());

    public Task<IReadOnlySet<Guid>> ListPausedOnOffboardingAsync(
        Guid tenantId,
        Guid fromUserId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlySet<Guid>>(
            Handovers
                .Where(h =>
                    h.TenantId == tenantId
                    && h.FromUserId == fromUserId
                    && h.Outcome == CampaignScheduleHandoverOutcome.Paused
                )
                .Select(h => h.ScheduleId)
                .ToHashSet()
        );

    public Task AddHandoverAsync(CampaignScheduleHandover handover, CancellationToken ct = default)
    {
        Handovers.Add(handover);
        return Task.CompletedTask;
    }

    private IEnumerable<CampaignSchedule> OpenRunAs(Guid tenantId, Guid userId) =>
        Schedules.Where(s => s.TenantId == tenantId && s.IsOpen && s.EffectiveRunAsUserId == userId);
}

internal sealed class FakeCampaignRepository : ICampaignRepository
{
    public List<Campaign> Campaigns { get; } = [];

    public Task<Campaign?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Campaigns.Find(c => c.TenantId == tenantId && c.Id == id));

    public Task<PagedResult<Campaign>> ListAsync(
        Guid tenantId,
        CampaignStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task AddAsync(Campaign campaign, CancellationToken ct = default)
    {
        Campaigns.Add(campaign);
        return Task.CompletedTask;
    }

    public void Remove(Campaign campaign) => Campaigns.Remove(campaign);
}

internal sealed class FakeUserPermissionsProjectionRepository : IUserPermissionsProjectionRepository
{
    public List<UserPermissionsProjection> Users { get; } = [];

    public UserPermissionsProjection Seed(Guid tenantId, Guid userId, params string[] permissionCodes)
    {
        var user = UserPermissionsProjection.Create(tenantId, userId, 1, permissionCodes, []);
        Users.Add(user);
        return user;
    }

    public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(Users.Find(u => u.TenantId == tenantId && u.UserId == userId));

    public Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default)
    {
        Users.Add(projection);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}

/// <summary>Toma el lease como lo hace el UPDATE atómico del repositorio real (no hay método de dominio para eso).</summary>
internal static class ScheduleLease
{
    public static Guid Take(CampaignSchedule schedule, DateTime untilUtc)
    {
        var token = Guid.NewGuid();
        typeof(CampaignSchedule).GetProperty(nameof(CampaignSchedule.LeaseToken))!.SetValue(schedule, token);
        typeof(CampaignSchedule).GetProperty(nameof(CampaignSchedule.LeasedUntilUtc))!.SetValue(schedule, untilUtc);
        return token;
    }
}
