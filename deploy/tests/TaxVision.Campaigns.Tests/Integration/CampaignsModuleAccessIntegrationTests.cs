using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.RateLimiting;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

namespace TaxVision.Campaigns.Tests.Integration;

/// <summary>
/// Oficina sin el módulo contra el SQL Server local: el claim de agendados deja fuera sus únicos (esperan a la
/// reactivación) pero sigue tomando sus recurrentes (el handler los salta), y el barrido de runs en Created no
/// la toma. Sin proyección todavía, todo sigue. Cada prueba usa tenants propios y los borra al terminar.
/// </summary>
public sealed class CampaignsModuleAccessIntegrationTests : IAsyncLifetime
{
    private readonly Guid _withoutModule = Guid.NewGuid();
    private readonly Guid _withModule = Guid.NewGuid();
    private readonly Guid _withoutProjection = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var tenantId in new[] { _withoutModule, _withModule, _withoutProjection })
        {
            await CampaignsTestDatabase.DeleteTenantAsync(tenantId);
            await CampaignsTestDatabase.ExecuteAsync(
                "DELETE FROM TenantPlanCodeProjections WHERE TenantId = @t",
                ("@t", tenantId)
            );
        }
    }

    [Fact]
    public async Task Claim_leaves_out_one_time_schedules_of_an_office_without_the_module()
    {
        await using var db = CampaignsTestDatabase.NewContext(Guid.Empty);
        SeedProjections(db);
        // Slot muy viejo: queda primero en el orden del claim aunque haya otros vencidos en la base.
        var slot = DateTime.UtcNow.AddDays(-30);
        var oneTimeWithout = OneTime(_withoutModule, slot);
        var recurringWithout = Recurring(_withoutModule, slot);
        var oneTimeWith = OneTime(_withModule, slot);
        var oneTimeUnknown = OneTime(_withoutProjection, slot);
        db.CampaignSchedules.AddRange(oneTimeWithout, recurringWithout, oneTimeWith, oneTimeUnknown);
        await db.SaveChangesAsync();

        var claimed = (await new CampaignScheduleRepository(db).ClaimDueAsync(DateTime.UtcNow, 120, 1000))
            .Select(c => c.Id)
            .ToHashSet();

        Assert.DoesNotContain(oneTimeWithout.Id, claimed);
        Assert.Contains(recurringWithout.Id, claimed);
        Assert.Contains(oneTimeWith.Id, claimed);
        Assert.Contains(oneTimeUnknown.Id, claimed);
    }

    [Fact]
    public async Task Resume_sweep_leaves_out_created_runs_of_an_office_without_the_module()
    {
        await using var db = CampaignsTestDatabase.NewContext(Guid.Empty);
        SeedProjections(db);
        var without = CreatedRun(_withoutModule);
        var with = CreatedRun(_withModule);
        var unknown = CreatedRun(_withoutProjection);
        db.CampaignRuns.AddRange(without, with, unknown);
        await db.SaveChangesAsync();

        var listed = (await new CampaignRunRepository(db).ListCreatedBeforeAsync(DateTime.UtcNow.AddMinutes(1), 1000))
            .Select(r => r.Id)
            .ToHashSet();

        Assert.DoesNotContain(without.Id, listed);
        Assert.Contains(with.Id, listed);
        Assert.Contains(unknown.Id, listed);
    }

    private void SeedProjections(DbContext db)
    {
        var without = TenantPlanCodeProjection.Create(_withoutModule, "enterprise", 1);
        without.ApplyIfNewer("enterprise", 2, ["clients", "sms"]);
        var with = TenantPlanCodeProjection.Create(_withModule, "enterprise", 1);
        with.ApplyIfNewer("enterprise", 2, ["clients", "Campaigns"]);
        db.Set<TenantPlanCodeProjection>().AddRange(without, with);
    }

    private static CampaignSchedule OneTime(Guid tenantId, DateTime slot) =>
        CampaignSchedule.CreateOneTime(tenantId, Guid.NewGuid(), slot, []).Value;

    private static CampaignSchedule Recurring(Guid tenantId, DateTime slot) =>
        CampaignSchedule.CreateRecurring(tenantId, Guid.NewGuid(), slot, RecurrenceFrequency.Daily, null, []).Value;

    private static CampaignRun CreatedRun(Guid tenantId) =>
        CampaignRun
            .Start(
                tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Manual",
                [new RunRecipientDraft("c", CampaignChannel.Email, "ana@x.com", null)]
            )
            .Value;
}
