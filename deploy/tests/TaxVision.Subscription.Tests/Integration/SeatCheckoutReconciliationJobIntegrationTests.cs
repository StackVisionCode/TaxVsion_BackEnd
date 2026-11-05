using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Infrastructure.Scheduling;
using TaxVision.Subscription.Tests.TestDoubles;
using Wolverine;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// SUB-2: si el consumidor gana la carrera con una intención, el conflicto no arrastra a las demás de la pasada.
/// </summary>
public sealed class SeatCheckoutReconciliationJobIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();

    public Task InitializeAsync() => _db.SeedActiveSubscriptionAsync();

    public Task DisposeAsync() => _db.CleanupAsync();

    [Fact]
    public async Task ConflictOnOneIntent_DoesNotBlockNext()
    {
        var intentA = await SeatCheckoutProvisioningConcurrencyIntegrationTests.SeedPendingIntentAsync(_db, quantity: 2);
        var intentB = await SeatCheckoutProvisioningConcurrencyIntegrationTests.SeedPendingIntentAsync(_db, quantity: 1);

        // Antes de responder por A, el "consumidor" la aprovisiona en otro contexto.
        var payments = new RacingPaymentClient(async () =>
        {
            await using var other = _db.NewContext();
            await SeatCheckoutProvisioningConcurrencyIntegrationTests.ProvisionInAsync(other, _db.TenantId, intentA);
            await other.SaveChangesAsync();
        });
        payments.RaceOn = await PaymentIdOfAsync(intentA);

        await using var provider = BuildProvider(payments);
        var job = new SeatCheckoutReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            null!,
            NullLogger<SeatCheckoutReconciliationJob>.Instance
        );

        await job.ReconcileAsync([intentA, intentB], DateTime.UtcNow, CancellationToken.None);

        await using var check = _db.NewContext();
        var statuses = await check
            .SeatPurchaseIntents.IgnoreQueryFilters()
            .Where(i => i.TenantId == _db.TenantId)
            .ToDictionaryAsync(i => i.Id, i => i.Status);
        Assert.Equal(SeatPurchaseIntentStatus.Provisioned, statuses[intentA]);
        Assert.Equal(SeatPurchaseIntentStatus.Provisioned, statuses[intentB]);
        Assert.Equal(3, await check.Seats.IgnoreQueryFilters().CountAsync(s => s.TenantId == _db.TenantId));
        Assert.Equal(
            2,
            await check.AuditLogs.CountAsync(a => a.TenantId == _db.TenantId && a.Action == "Seats.Purchased")
        );
    }

    private async Task<Guid> PaymentIdOfAsync(Guid intentId)
    {
        await using var db = _db.NewContext();
        return (await db.SeatPurchaseIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == intentId)).SaaSPaymentId!.Value;
    }

    private ServiceProvider BuildProvider(ISeatCheckoutPaymentClient payments)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _db.NewContext());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SubscriptionDbContext>());
        services.AddScoped<ISeatPurchaseIntentRepository, SeatPurchaseIntentRepository>();
        services.AddScoped<ISubscriptionRepository, TenantSubscriptionRepository>();
        services.AddScoped<ISubscriptionSeatRepository, SubscriptionSeatRepository>();
        services.AddScoped<ISubscriptionAuditLogWriter, SubscriptionAuditLogWriter>();
        services.AddSingleton<ISubscriptionMetrics>(new FakeSubscriptionMetrics());
        services.AddSingleton<IMessageBus>(new CapturingMessageBus());
        services.AddSingleton(payments);
        return services.BuildServiceProvider();
    }

    private sealed class RacingPaymentClient(Func<Task> race) : ISeatCheckoutPaymentClient
    {
        public Guid RaceOn { get; set; }

        public Task<Result<SeatCheckoutClientResult>> CreateCheckoutAsync(
            SeatCheckoutClientRequest request,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public async Task<SeatPaymentStatusResult?> GetPaymentStatusAsync(
            Guid tenantId,
            Guid saaSPaymentId,
            CancellationToken ct = default
        )
        {
            if (saaSPaymentId == RaceOn)
                await race();
            return new SeatPaymentStatusResult("Succeeded", DateTime.UtcNow);
        }
    }
}
