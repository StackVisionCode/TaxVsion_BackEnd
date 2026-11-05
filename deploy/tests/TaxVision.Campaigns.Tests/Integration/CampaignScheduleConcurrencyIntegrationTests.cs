using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Tests.Integration;

/// <summary>CMP-01 (CA-6) contra el SQL Server local: dos escritores del mismo agendado con lecturas viejas.</summary>
public sealed class CampaignScheduleConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _runId = Guid.NewGuid();
    private Guid _scheduleId;

    public async Task InitializeAsync()
    {
        var schedule = CampaignSchedule
            .CreateRecurring(_tenantId, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1), RecurrenceFrequency.Hourly, null, [])
            .Value;
        schedule.MarkFired(_runId, DateTime.UtcNow);
        _scheduleId = schedule.Id;

        await using var db = CampaignsTestDatabase.NewContext(_tenantId);
        db.CampaignSchedules.Add(schedule);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => CampaignsTestDatabase.DeleteTenantAsync(_tenantId);

    [Fact]
    public async Task ReleaseAndMarkFired_FromStaleReads_SecondThrowsConcurrency()
    {
        await using var first = CampaignsTestDatabase.NewContext(_tenantId);
        await using var second = CampaignsTestDatabase.NewContext(_tenantId);
        var a = await first.CampaignSchedules.SingleAsync(s => s.Id == _scheduleId);
        var b = await second.CampaignSchedules.SingleAsync(s => s.Id == _scheduleId);

        a.ReleaseActiveRun(_runId);
        await first.SaveChangesAsync();

        b.MarkFired(Guid.NewGuid(), DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
