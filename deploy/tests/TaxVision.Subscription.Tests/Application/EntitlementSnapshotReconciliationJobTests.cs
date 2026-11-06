using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Infrastructure.Scheduling;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

// SUB-4: la anti-entropía encola el recálculo de cada tenant (solo si cambió) por el outbox; un snapshot que se
// quedó atrás (recálculo perdido) se cura en la pasada siguiente sin que el tenant cambie nada.
public sealed class EntitlementSnapshotReconciliationJobTests
{
    [Fact]
    public async Task StaleSnapshot_IsRecalculated()
    {
        var plan = AddOnTestCatalog.PlanWith("starter", ["customers"]);
        var tenantId = Guid.NewGuid();
        var subscription = AddOnTestCatalog.ActiveSubscription(tenantId, plan);
        var otherTenant = AddOnTestCatalog.ActiveSubscription(Guid.NewGuid(), plan);
        var databaseName = $"entitlement-reconciliation-{Guid.NewGuid():N}";
        await using (var seed = NewContext(databaseName))
        {
            seed.Subscriptions.AddRange(subscription, otherTenant);
            await seed.SaveChangesAsync();
        }

        var recording = new OutboxRecording();
        await using var provider = BuildProvider(databaseName, recording);
        var job = new EntitlementSnapshotReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            null!,
            NullLogger<EntitlementSnapshotReconciliationJob>.Instance,
            Options.Create(new EntitlementSnapshotReconciliationOptions { BatchSize = 1 })
        );

        var queued = await job.EnqueueAllAsync(CancellationToken.None);

        Assert.Equal(2, queued);
        var command = Assert.Single(
            recording.Flushed.OfType<RecalculateEntitlementsCommand>(),
            c => c.TenantId == tenantId
        );
        Assert.True(command.OnlyIfChanged);
        Assert.Equal(2, recording.Commits);

        // El tenant compró un add-on y su recálculo se perdió: el snapshot vigente no lo tiene.
        var stale = (
            await RecalculateEntitlementsHandlerTests.RecalculateAsync(
                subscription,
                plan,
                [],
                new FakeSnapshotRepo(),
                new CapturingMessageBus()
            )
        ).Upserted!;
        var addOn = AddOnTestCatalog.ModuleAddOn("signature.pack", "signature");

        var healed = await RecalculateEntitlementsHandlerTests.RecalculateAsync(
            subscription,
            plan,
            [addOn],
            new FakeSnapshotRepo(stale),
            new CapturingMessageBus(),
            command.OnlyIfChanged
        );

        Assert.Equal("True", healed.Upserted!.FindByKey("module.signature")?.Value, ignoreCase: true);
    }

    private static SubscriptionDbContext NewContext(string databaseName, ITenantContext? tenant = null) =>
        new(
            new DbContextOptionsBuilder<SubscriptionDbContext>()
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            tenant ?? new SettableTenantContext()
        );

    private static ServiceProvider BuildProvider(string databaseName, OutboxRecording recording)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITenantContext, SettableTenantContext>();
        services.AddScoped(sp => NewContext(databaseName, sp.GetRequiredService<ITenantContext>()));
        services.AddScoped<ISubscriptionRepository, TenantSubscriptionRepository>();
        RecordingSubscriptionOutbox.AddTo(services, recording);
        return services.BuildServiceProvider();
    }
}
