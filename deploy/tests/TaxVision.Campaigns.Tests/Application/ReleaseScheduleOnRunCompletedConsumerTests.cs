using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Consumers;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
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

    /// <summary>CMP-14: ni el agendado ni el run existen todavía; se reintenta en vez de acusarlo como procesado.</summary>
    [Fact]
    public async Task Handle_RunNotPersistedYet_ThrowsDependencyNotReady()
    {
        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            ReleaseScheduleOnRunCompletedConsumer.Handle(
                Completed(Guid.NewGuid()),
                new FakeCampaignScheduleRepository(),
                new FakeCampaignRunRepository(),
                new FakeUnitOfWork(),
                NullLogger<CampaignSchedule>.Instance,
                CancellationToken.None
            )
        );
    }

    /// <summary>Un envío manual existe y no tiene agendado: no hay nada que liberar ni que esperar.</summary>
    [Fact]
    public async Task Handle_ManualRun_HasNothingToRelease()
    {
        var run = CampaignRun
            .Start(
                Tenant,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Manual",
                [new RunRecipientDraft("c1", CampaignChannel.Email, "a@x.com", null)]
            )
            .Value;
        var runs = new FakeCampaignRunRepository();
        runs.Runs.Add(run);
        var unitOfWork = new FakeUnitOfWork();

        await ReleaseScheduleOnRunCompletedConsumer.Handle(
            Completed(run.Id),
            new FakeCampaignScheduleRepository(),
            runs,
            unitOfWork,
            NullLogger<CampaignSchedule>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
