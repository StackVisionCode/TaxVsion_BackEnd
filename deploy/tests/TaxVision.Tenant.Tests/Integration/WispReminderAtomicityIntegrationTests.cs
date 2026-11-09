using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Infrastructure.Compliance;
using TaxVision.Tenant.Infrastructure.Scheduling;
using Wolverine;
using Wolverine.Transports.Tcp;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>TEN-1 contra SQL real: el aviso del WISP y la fecha anotada se confirman juntos o no queda ninguno.</summary>
public sealed class WispReminderAtomicityIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime SavedAt = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 12, 2, 12, 0, 0, DateTimeKind.Utc);

    private readonly TenantTemporaryDatabase _database = new();
    private readonly FailAfterSaveInterceptor _failAfterSave = new();
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        try
        {
            _host = await TenantWolverineHost.StartAsync(
                _database.ConnectionString,
                (options, closedPort) =>
                    options
                        .PublishMessage<OfficeWispReviewDueIntegrationEvent>()
                        .ToPort(closedPort)
                        .UseDurableOutbox(),
                _failAfterSave
            );
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        await TenantWolverineHost.StopAsync(_host);
        await _database.DropAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task FailureAfterSave_LeavesNoReminderAndNoEnvelope()
    {
        var tenantId = await SeedOverdueOfficeAsync();
        _failAfterSave.Armed = true;

        var sent = await NewJob().SweepAsync(batchSize: 10, Now, CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Null(await ReminderSentOnAsync(tenantId));
        Assert.Equal(0, await OutgoingAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task Success_PersistsReminderAndEnvelopeTogether()
    {
        var tenantId = await SeedOverdueOfficeAsync();

        var sent = await NewJob().SweepAsync(batchSize: 10, Now, CancellationToken.None);

        Assert.Equal(1, sent);
        Assert.Equal(new DateOnly(2026, 12, 2), await ReminderSentOnAsync(tenantId));
        Assert.Equal(1, await OutgoingAsync());
    }

    private Task<int> OutgoingAsync() =>
        _database.CountEnvelopesAsync("wolverine_outgoing_envelopes", nameof(OfficeWispReviewDueIntegrationEvent));

    private async Task<DateOnly?> ReminderSentOnAsync(Guid tenantId)
    {
        await using var db = _database.NewContext();
        return await db
            .OfficeCompliances.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .Select(c => c.WispReminderSentOn)
            .SingleAsync();
    }

    private async Task<Guid> SeedOverdueOfficeAsync()
    {
        var tenant = DomainTenant.Create("Wisp Tax", $"wr-{Guid.NewGuid():N}"[..20], "Etc/UTC", DateTime.UtcNow).Value;
        var compliance = OfficeCompliance.CreateEmpty(tenant.Id);
        compliance.Update(
            new OfficeComplianceDraft(null, "2025-12-01", null, null, null, 7),
            EfinChange.Keep,
            Guid.NewGuid(),
            SavedAt
        );

        await using var db = _database.NewContext();
        db.Tenants.Add(tenant);
        db.OfficeCompliances.Add(compliance);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private WispReviewReminderJob NewJob() =>
        new(
            _host!.Services.GetRequiredService<IServiceScopeFactory>(),
            new NoLock(),
            Options.Create(new WispReviewReminderOptions()),
            NullLogger<WispReviewReminderJob>.Instance
        );

    private sealed class NoLock : IDistributedLockFactory
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            throw new NotSupportedException("SweepAsync runs without the lock in these tests.");
    }
}
