using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Consumers;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

public sealed class ReleaseScheduleOnRunCompletedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static CampaignRunCompletedIntegrationEvent Completed(Guid runId) =>
        new()
        {
            TenantId = Tenant,
            CorrelationId = "test",
            CampaignId = Guid.NewGuid(),
            RunId = runId,
            TerminalStatus = "Completed",
            RecipientCount = 1,
            Delivered = 1,
            Accepted = 0,
            Failed = 0,
            Skipped = 0,
            Unknown = 0,
        };

    [Fact]
    public async Task Handle_NotFoundByActiveRun_ReleasesByRunScheduleId()
    {
        var runId = Guid.NewGuid();
        var schedule = CampaignSchedule
            .CreateRecurring(Tenant, Guid.NewGuid(), DateTime.UtcNow, RecurrenceFrequency.Hourly, null, [])
            .Value;
        schedule.MarkFired(runId, DateTime.UtcNow, runIsTerminal: false);
        var schedules = new FakeCampaignScheduleRepository { ActiveRunLookupMisses = true };
        schedules.Schedules.Add(schedule);
        var runs = new FakeCampaignRunRepository();
        runs.ScheduleIdByRun[runId] = schedule.Id;
        var unitOfWork = new FakeUnitOfWork();

        await ReleaseScheduleOnRunCompletedConsumer.Handle(
            Completed(runId),
            schedules,
            runs,
            unitOfWork,
            NullLogger<CampaignSchedule>.Instance,
            CancellationToken.None
        );

        Assert.Null(schedule.ActiveRunId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }
}
