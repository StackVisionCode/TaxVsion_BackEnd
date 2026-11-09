using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Infrastructure.Compliance;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Scheduling;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Infrastructure;

/// <summary>Pasada del job del WISP: una unidad por oficina, y una colisión no corta el resto.</summary>
public sealed class WispReviewReminderJobTests
{
    private static readonly DateTime SavedAt = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 12, 2, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryOfficeComplianceRepository _compliances = new();
    private readonly OutboxRecording _outbox = new();

    [Fact]
    public async Task A_sweep_pages_through_every_candidate_with_one_unit_each()
    {
        var tenants = Enumerable.Range(0, 5).Select(_ => Seed("2025-12-01")).ToList();
        Seed("2026-06-01"); // fuera de ventana
        Seed(null); // sin WISP

        var sent = await NewJob().SweepAsync(batchSize: 2, Now, CancellationToken.None);

        Assert.Equal(5, sent);
        Assert.Equal(5, _outbox.Flushes);
        var events = _outbox.Flushed.OfType<OfficeWispReviewDueIntegrationEvent>().ToList();
        Assert.Equal(tenants.ToHashSet(), events.Select(e => e.OfficeTenantId).ToHashSet());
        Assert.All(events, e => Assert.Equal(OfficeWispReviewStages.Overdue, e.Stage));
        Assert.Single(events.Select(e => e.CorrelationId).Distinct());
    }

    [Fact]
    public async Task A_second_sweep_the_same_day_sends_nothing()
    {
        Seed("2025-12-01");
        var job = NewJob();

        await job.SweepAsync(batchSize: 10, Now, CancellationToken.None);
        var again = await job.SweepAsync(batchSize: 10, Now.AddHours(3), CancellationToken.None);

        Assert.Equal(0, again);
        Assert.Single(_outbox.Flushed);
    }

    [Fact]
    public async Task A_conflict_skips_that_office_without_publishing_and_the_sweep_continues()
    {
        Seed("2025-12-01");
        Seed("2025-12-01");
        _outbox.ConflictOnFlush.Add(1);

        var sent = await NewJob().SweepAsync(batchSize: 10, Now, CancellationToken.None);

        Assert.Equal(1, sent);
        Assert.Equal(2, _outbox.Flushes);
        Assert.Single(_outbox.Flushed);
    }

    private Guid Seed(string? adoptedOn)
    {
        var compliance = OfficeCompliance.CreateEmpty(Guid.NewGuid());
        compliance.Update(
            new OfficeComplianceDraft(null, adoptedOn, null, null, null, 7),
            EfinChange.Keep,
            Guid.NewGuid(),
            SavedAt
        );
        _compliances.Compliances.Add(compliance);
        return compliance.TenantId;
    }

    private WispReviewReminderJob NewJob()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection()
            .AddSingleton<IOfficeComplianceRepository>(_compliances)
            .AddSingleton<ICorrelationContext, NoopCorrelationContext>()
            .AddSingleton<ITenantContext, NoTenantContext>()
            .AddDbContext<TenantDbContext>(db =>
                db.UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            );
        RecordingTenantOutbox.AddTo(services, _outbox);

        return new WispReviewReminderJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new NoLock(),
            Options.Create(new WispReviewReminderOptions()),
            NullLogger<WispReviewReminderJob>.Instance
        );
    }

    private sealed class NoLock : IDistributedLockFactory
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            throw new NotSupportedException("SweepAsync runs without the lock in these tests.");
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
