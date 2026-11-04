using TaxVision.Campaigns.Application.Scheduling.Queries;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

/// <summary>Pre-flight del retiro en Campaigns: agendados vivos (activos o pausados) que disparan en nombre del empleado.</summary>
public sealed class GetCampaignsOffboardingImpactHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeCampaignScheduleRepository _schedules = new();

    [Fact]
    public async Task Counts_active_and_paused_schedules_that_fire_as_the_user()
    {
        Seed(_userId);
        Seed(_userId).Pause();
        Seed(_userId).Cancel();
        Seed(Guid.NewGuid());
        Seed(_userId, Guid.NewGuid());
        Seed(Guid.NewGuid()).HandOverOnOffboarding(_userId, successorCanViewAllCustomers: false);

        var result = await HandleAsync(_userId);

        Assert.Equal(3, result.ActiveSchedules);
        Assert.Equal(0, (await HandleAsync(Guid.Empty)).ActiveSchedules);
    }

    private CampaignSchedule Seed(Guid creator, Guid? tenantId = null)
    {
        var schedule = CampaignSchedule
            .CreateOneTime(
                tenantId ?? _tenantId,
                Guid.NewGuid(),
                DateTime.UtcNow.AddDays(1),
                [],
                createdByUserId: creator
            )
            .Value;
        _schedules.Schedules.Add(schedule);
        return schedule;
    }

    private Task<CampaignsOffboardingImpactResponse> HandleAsync(Guid userId) =>
        GetCampaignsOffboardingImpactHandler.Handle(
            new GetCampaignsOffboardingImpactQuery(_tenantId, userId),
            _schedules,
            CancellationToken.None
        );
}
