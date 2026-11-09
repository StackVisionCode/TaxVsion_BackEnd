using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Sms.Application.Messages.Queries;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Infrastructure.Persistence;

namespace TaxVision.Sms.Tests.Application;

/// <summary>Estadísticas y filtro del listado del CRM: un SMS de resultado desconocido se cuenta y se filtra.</summary>
public sealed class SmsReadServiceTests
{
    private const string Source = "crm-sms";
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    private static SmsDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<SmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

    private static SmsMessage Message(Guid tenantId, string key)
    {
        var message = SmsMessage
            .Create(
                tenantId,
                Guid.NewGuid(),
                PhoneE164.Create("+18095551234").Value,
                SmsBody.Create("hola").Value,
                key,
                "corr",
                Guid.NewGuid(),
                "fake",
                Source,
                [],
                Now
            )
            .Value;
        message.MarkSendAttempted(Now);
        return message;
    }

    private static async Task<(SmsDbContext Db, Guid Tenant)> SeedAsync()
    {
        var db = NewDb();
        var tenant = Guid.NewGuid();
        var unknown = Message(tenant, "k-unknown");
        unknown.MarkOutcomeUnknown(Now, null, "sms.providerOutcomeUnknown", "timeout");
        var accepted = Message(tenant, "k-accepted");
        accepted.MarkAccepted("pm-1", Now);
        db.SmsMessages.AddRange(unknown, accepted);
        await db.SaveChangesAsync();
        return (db, tenant);
    }

    [Fact]
    public async Task GetStats_counts_outcome_unknown()
    {
        var (db, tenant) = await SeedAsync();

        var stats = await new SmsReadService(db).GetStatsAsync(tenant, Now.AddDays(-1), Now.AddDays(1), Source);

        Assert.Equal(2, stats.Total);
        Assert.Equal(1, stats.OutcomeUnknown);
        Assert.Equal(1, stats.Accepted);
        Assert.Equal(0, stats.Pending);
    }

    [Fact]
    public async Task SearchMessages_filters_by_outcome_unknown()
    {
        var (db, tenant) = await SeedAsync();

        var page = await new SmsReadService(db).SearchMessagesAsync(
            tenant,
            customerId: null,
            Enum.Parse<SmsMessageStatusFilter>("OutcomeUnknown"),
            term: null,
            fromUtc: null,
            toUtc: null,
            sourceContext: Source,
            page: 1,
            size: 20
        );

        var item = Assert.Single(page.Items);
        Assert.Equal(SmsMessageStatus.OutcomeUnknown, item.Status);
    }
}
