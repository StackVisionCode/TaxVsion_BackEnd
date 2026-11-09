using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

namespace TaxVision.Campaigns.Tests.Integration;

/// <summary>
/// CMP-02 contra el SQL Server local: la autocuración libera los agendados cuyo run activo ya terminó y
/// pausa los que ya pasaron su horario (D-CMP-1). El barrido es de todos los tenants, así que corre en una
/// transacción que se revierte: no toca datos ajenos a la prueba.
/// </summary>
public sealed class CampaignScheduleRepositoryIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);
    private readonly Guid _tenantId = Guid.NewGuid();
    private CampaignSchedule _overdue = default!;
    private CampaignSchedule _inFlight = default!;
    private CampaignSchedule _future = default!;

    public async Task InitializeAsync()
    {
        var completedA = ClosedRun();
        var dispatching = CampaignRun
            .Start(
                _tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Scheduled",
                [new RunRecipientDraft("c", CampaignChannel.Email, "a@x.com", null)]
            )
            .Value;
        var completedC = ClosedRun();

        _overdue = Fired(RecurrenceFrequency.Hourly, completedA.Id);
        _inFlight = Fired(RecurrenceFrequency.Hourly, dispatching.Id);
        _future = Fired(RecurrenceFrequency.Daily, completedC.Id);

        await using var db = CampaignsTestDatabase.NewContext(_tenantId);
        db.CampaignRuns.AddRange(completedA, dispatching, completedC);
        db.CampaignSchedules.AddRange(_overdue, _inFlight, _future);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => CampaignsTestDatabase.DeleteTenantAsync(_tenantId);

    [Fact]
    public async Task ReleaseStuck_ActiveRunTerminal_ClearsActiveRunId()
    {
        await using var db = CampaignsTestDatabase.NewContext(_tenantId);
        await using var tx = await db.Database.BeginTransactionAsync();
        var repository = new CampaignScheduleRepository(db);

        // Dentro del margen: el consumer de run.completed todavía puede liberarlo.
        await repository.ReleaseStuckAsync(DateTime.UtcNow, Grace);
        Assert.Equal(_overdue.ActiveRunId, (await ReadAsync(db, _overdue.Id)).ActiveRunId);

        // 30 min después: el próximo slot del horario ya pasó; el diario todavía no.
        await repository.ReleaseStuckAsync(DateTime.UtcNow.AddMinutes(30), Grace);

        var overdue = await ReadAsync(db, _overdue.Id);
        Assert.Null(overdue.ActiveRunId);
        Assert.Equal(ScheduleStatus.Paused, overdue.Status);
        Assert.Equal(SchedulePauseReason.MissedSlot, overdue.PauseReason);

        var future = await ReadAsync(db, _future.Id);
        Assert.Null(future.ActiveRunId);
        Assert.Equal(ScheduleStatus.Active, future.Status);

        var inFlight = await ReadAsync(db, _inFlight.Id);
        Assert.Equal(_inFlight.ActiveRunId, inFlight.ActiveRunId);
        Assert.Equal(ScheduleStatus.Active, inFlight.Status);

        await tx.RollbackAsync();
    }

    private static Task<CampaignSchedule> ReadAsync(DbContext db, Guid id) =>
        db.Set<CampaignSchedule>().AsNoTracking().SingleAsync(s => s.Id == id);

    // Run cerrado en Start (unidad sin destino → Skipped → Completed).
    private CampaignRun ClosedRun() =>
        CampaignRun
            .Start(
                _tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Scheduled",
                [new RunRecipientDraft("c", CampaignChannel.Email, null, null)]
            )
            .Value;

    private CampaignSchedule Fired(RecurrenceFrequency frequency, Guid runId)
    {
        var schedule = CampaignSchedule
            .CreateRecurring(_tenantId, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-59), frequency, null, [])
            .Value;
        schedule.MarkFired(runId, DateTime.UtcNow, runIsTerminal: false);
        return schedule;
    }
}
