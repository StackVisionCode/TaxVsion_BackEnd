using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Scheduling;
using TaxVision.Subscription.Tests.TestDoubles;
using Wolverine;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>SUB-2: un conflicto al expirar una intención no impide expirar las demás de la pasada.</summary>
public sealed class CheckoutIntentExpirationJobIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.CleanupAsync();

    [Fact]
    public async Task FailureOnOneIntent_DoesNotBlockOthers()
    {
        var created = DateTime.UtcNow.AddHours(-2);
        var expiredAt = DateTime.UtcNow.AddMinutes(-10);
        var seat = SeatPurchaseIntent
            .Create(
                _db.TenantId,
                SeatType.Standard,
                1,
                true,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                Money.FromCents(1500, "USD"),
                Guid.NewGuid(),
                created
            )
            .Value;
        seat.AttachCheckout(Guid.NewGuid(), "https://checkout.test/seat", expiredAt, created);
        var addOn = AddOnPurchaseIntent
            .Create(
                _db.TenantId,
                AddOnTestCatalog.QuotaAddOn("test.pack"),
                1,
                true,
                Money.Create(29m, "USD").Value,
                BillingCycle.Monthly,
                Money.FromCents(2900, "USD"),
                Guid.NewGuid(),
                created
            )
            .Value;
        addOn.AttachCheckout(Guid.NewGuid(), "https://checkout.test/addon", expiredAt, created);
        var renewal = SubscriptionRenewalIntent
            .Create(_db.TenantId, Money.FromCents(4900, "USD"), BillingCycle.Monthly, Guid.NewGuid(), created)
            .Value;
        renewal.AttachCheckout(Guid.NewGuid(), "https://checkout.test/renewal", expiredAt, created);
        await using (var seed = _db.NewContext())
        {
            seed.SeatPurchaseIntents.Add(seat);
            seed.AddOnPurchaseIntents.Add(addOn);
            seed.SubscriptionRenewalIntents.Add(renewal);
            await seed.SaveChangesAsync();
        }

        // Mientras el job cancela la sesión de asientos, el webhook la marca pagada en otro contexto.
        var cancellation = new RacingCancellationClient(
            seat.SaaSPaymentId!.Value,
            async () =>
            {
                await using var other = _db.NewContext();
                var paid = await other.SeatPurchaseIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == seat.Id);
                paid.MarkPaid(DateTime.UtcNow);
                await other.SaveChangesAsync();
            }
        );

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _db.NewContext());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SubscriptionDbContext>());
        services.AddScoped<ICorrelationContext, FakeCorrelationContext>();
        services.AddSingleton<IHostedCheckoutCancellationClient>(cancellation);
        // BB-3: cada intención es una unidad con outbox.
        RecordingSubscriptionOutbox.AddTo(services, new OutboxRecording());
        services.AddScoped<BuildingBlocks.Tenancy.ITenantContext, SettableTenantContext>();
        await using var provider = services.BuildServiceProvider();
        var job = new CheckoutIntentExpirationJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            null!,
            NullLogger<CheckoutIntentExpirationJob>.Instance,
            Options.Create(new CheckoutIntentExpirationOptions())
        );

        await job.ExpireAsync(
            new CheckoutIntentExpirationJob.CheckoutExpirationBatch([seat.Id], [addOn.Id], [renewal.Id], []),
            "test",
            DateTime.UtcNow,
            CancellationToken.None
        );

        await using var check = _db.NewContext();
        Assert.Equal(
            SeatPurchaseIntentStatus.Paid,
            (await check.SeatPurchaseIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == seat.Id)).Status
        );
        Assert.Equal(
            AddOnPurchaseIntentStatus.Expired,
            (await check.AddOnPurchaseIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == addOn.Id)).Status
        );
        Assert.Equal(
            SubscriptionRenewalIntentStatus.Expired,
            (await check.SubscriptionRenewalIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == renewal.Id)).Status
        );
    }

    // SUB-7: sin esta pasada, un corte entre PaymentApp y el adjunto dejaba la intención Pending para siempre.
    [Fact]
    public async Task IntentsWithoutSession_AreClosed_AndAttachedOnesAreKept()
    {
        var created = DateTime.UtcNow.AddHours(-2);
        var seat = NewSeatIntent(created);
        var attached = NewSeatIntent(created);
        attached.AttachCheckout(Guid.NewGuid(), "https://checkout.test/seat", DateTime.UtcNow.AddHours(1), created);
        var addOn = AddOnPurchaseIntent
            .Create(
                _db.TenantId,
                AddOnTestCatalog.QuotaAddOn("test.pack"),
                1,
                true,
                Money.Create(29m, "USD").Value,
                BillingCycle.Monthly,
                Money.FromCents(2900, "USD"),
                Guid.NewGuid(),
                created
            )
            .Value;
        var renewal = SubscriptionRenewalIntent
            .Create(_db.TenantId, Money.FromCents(4900, "USD"), BillingCycle.Monthly, Guid.NewGuid(), created)
            .Value;
        await using (var seed = _db.NewContext())
        {
            seed.SeatPurchaseIntents.AddRange(seat, attached);
            seed.AddOnPurchaseIntents.Add(addOn);
            seed.SubscriptionRenewalIntents.Add(renewal);
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _db.NewContext());
        RecordingSubscriptionOutbox.AddTo(services, new OutboxRecording());
        await using var provider = services.BuildServiceProvider();
        var job = new CheckoutIntentExpirationJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            null!,
            NullLogger<CheckoutIntentExpirationJob>.Instance,
            Options.Create(new CheckoutIntentExpirationOptions())
        );

        var closed = await job.CloseUnattachedAsync(
            new CheckoutIntentExpirationJob.UnattachedBatch([seat.Id, attached.Id], [addOn.Id], [renewal.Id], []),
            DateTime.UtcNow,
            CancellationToken.None
        );

        Assert.Equal(3, closed);
        await using var check = _db.NewContext();
        Assert.Equal(
            SeatPurchaseIntentStatus.Failed,
            (await check.SeatPurchaseIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == seat.Id)).Status
        );
        Assert.Equal(
            SeatPurchaseIntentStatus.Pending,
            (await check.SeatPurchaseIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == attached.Id)).Status
        );
        Assert.Equal(
            AddOnPurchaseIntentStatus.Failed,
            (await check.AddOnPurchaseIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == addOn.Id)).Status
        );
        Assert.Equal(
            SubscriptionRenewalIntentStatus.Failed,
            (await check.SubscriptionRenewalIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == renewal.Id)).Status
        );
    }

    // SUB-13: el upgrade por redirect sin sesión se cierra; el off-session sin URL es un cobro en curso.
    [Fact]
    public async Task HostedUpgradeWithoutSession_IsClosed_OffSessionIsKept()
    {
        var hosted = await SeedUpgradeAsync(_db, usesHostedCheckout: true);
        var offSessionDb = new SubscriptionIntegrationDb();
        try
        {
            var offSession = await SeedUpgradeAsync(offSessionDb, usesHostedCheckout: false);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => _db.NewContext());
            RecordingSubscriptionOutbox.AddTo(services, new OutboxRecording());
            await using var provider = services.BuildServiceProvider();
            var job = new CheckoutIntentExpirationJob(
                provider.GetRequiredService<IServiceScopeFactory>(),
                null!,
                NullLogger<CheckoutIntentExpirationJob>.Instance,
                Options.Create(new CheckoutIntentExpirationOptions())
            );

            var closed = await job.CloseUnattachedAsync(
                new CheckoutIntentExpirationJob.UnattachedBatch([], [], [], [hosted, offSession]),
                DateTime.UtcNow,
                CancellationToken.None
            );

            Assert.Equal(1, closed);
            await using var check = _db.NewContext();
            var statuses = await check
                .Subscriptions.IgnoreQueryFilters()
                .SelectMany(s => s.PlanChangeRequests)
                .Where(r => r.Id == hosted || r.Id == offSession)
                .ToDictionaryAsync(r => r.Id, r => r.Status);
            Assert.Equal(PlanChangeRequestStatus.Expired, statuses[hosted]);
            Assert.Equal(PlanChangeRequestStatus.AwaitingPayment, statuses[offSession]);
        }
        finally
        {
            await DeleteUpgradesAsync(offSessionDb);
            await offSessionDb.CleanupAsync();
            await DeleteUpgradesAsync(_db);
        }
    }

    // SUB-15: la sesión venció sin pagarse; el barrido devuelve la cancelación que el upgrade había quitado.
    [Fact]
    public async Task ExpiredUpgradeSession_KeepsTheScheduledCancellation()
    {
        var upgrade = await SeedUpgradeAsync(
            _db,
            usesHostedCheckout: true,
            scheduleCancellation: true,
            attachSession: true
        );
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => _db.NewContext());
            RecordingSubscriptionOutbox.AddTo(services, new OutboxRecording());
            services.AddSingleton<IHostedCheckoutCancellationClient>(
                new RacingCancellationClient(Guid.Empty, () => Task.CompletedTask)
            );
            await using var provider = services.BuildServiceProvider();
            var job = new CheckoutIntentExpirationJob(
                provider.GetRequiredService<IServiceScopeFactory>(),
                null!,
                NullLogger<CheckoutIntentExpirationJob>.Instance,
                Options.Create(new CheckoutIntentExpirationOptions())
            );

            var expired = await job.ExpireAsync(
                new CheckoutIntentExpirationJob.CheckoutExpirationBatch([], [], [], [upgrade]),
                "test",
                DateTime.UtcNow,
                CancellationToken.None
            );

            Assert.Equal(1, expired);
            await AssertCancellationKeptAsync(upgrade);
        }
        finally
        {
            await DeleteUpgradesAsync(_db);
        }
    }

    // SUB-15: lo mismo si la sesión nunca se adjuntó.
    [Fact]
    public async Task UnattachedUpgrade_KeepsTheScheduledCancellation()
    {
        var upgrade = await SeedUpgradeAsync(_db, usesHostedCheckout: true, scheduleCancellation: true);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => _db.NewContext());
            RecordingSubscriptionOutbox.AddTo(services, new OutboxRecording());
            await using var provider = services.BuildServiceProvider();
            var job = new CheckoutIntentExpirationJob(
                provider.GetRequiredService<IServiceScopeFactory>(),
                null!,
                NullLogger<CheckoutIntentExpirationJob>.Instance,
                Options.Create(new CheckoutIntentExpirationOptions())
            );

            var closed = await job.CloseUnattachedAsync(
                new CheckoutIntentExpirationJob.UnattachedBatch([], [], [], [upgrade]),
                DateTime.UtcNow,
                CancellationToken.None
            );

            Assert.Equal(1, closed);
            await AssertCancellationKeptAsync(upgrade);
        }
        finally
        {
            await DeleteUpgradesAsync(_db);
        }
    }

    private async Task AssertCancellationKeptAsync(Guid upgrade)
    {
        await using var check = _db.NewContext();
        var subscription = await check
            .Subscriptions.IgnoreQueryFilters()
            .Include(s => s.PlanChangeRequests)
            .SingleAsync(s => s.PlanChangeRequests.Any(r => r.Id == upgrade));
        Assert.Equal(PlanChangeRequestStatus.Expired, subscription.PlanChangeRequests.Single().Status);
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Equal("too expensive", subscription.CancellationReason);
    }

    private static async Task<Guid> SeedUpgradeAsync(
        SubscriptionIntegrationDb db,
        bool usesHostedCheckout,
        bool scheduleCancellation = false,
        bool attachSession = false
    )
    {
        var seeded = await db.SeedActiveSubscriptionAsync(publishedPlan: true);
        await using var context = db.NewContext();
        var subscription = await context
            .Subscriptions.IgnoreQueryFilters()
            .Include(s => s.PlanChangeRequests)
            .SingleAsync(s => s.Id == seeded.Id);
        var plan = await context.Plans.AsNoTracking().Include(p => p.Versions).SingleAsync(p => p.Id == seeded.PlanId);
        if (scheduleCancellation)
            subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow.AddHours(-3));
        var upgrade = subscription.RequestUpgrade(
            plan,
            plan.GetPublishedVersion()!,
            BillingCycle.Monthly,
            4900,
            "USD",
            $"plan-change-{Guid.NewGuid():N}",
            Guid.Empty,
            DateTime.UtcNow.AddHours(-2),
            usesHostedCheckout
        );
        Assert.True(upgrade.IsSuccess, upgrade.IsFailure ? upgrade.Error.Code : null);
        var requestId = subscription.PlanChangeRequests.Single().Id;
        if (attachSession)
            subscription.AttachUpgradeCheckout(
                requestId,
                Guid.NewGuid(),
                "https://checkout.test/upgrade",
                DateTime.UtcNow.AddMinutes(-10),
                Guid.Empty,
                DateTime.UtcNow
            );
        await context.SaveChangesAsync();
        return requestId;
    }

    private static async Task DeleteUpgradesAsync(SubscriptionIntegrationDb db)
    {
        await using var connection = new SqlConnection(SubscriptionIntegrationDb.Connection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PlanChangeRequests WHERE TenantId = @tenant";
        command.Parameters.AddWithValue("@tenant", db.TenantId);
        await command.ExecuteNonQueryAsync();
    }

    private SeatPurchaseIntent NewSeatIntent(DateTime created) =>
        SeatPurchaseIntent
            .Create(
                _db.TenantId,
                SeatType.Standard,
                1,
                true,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                Money.FromCents(1500, "USD"),
                Guid.NewGuid(),
                created
            )
            .Value;

    private sealed class RacingCancellationClient(Guid raceOn, Func<Task> race) : IHostedCheckoutCancellationClient
    {
        public async Task<Result> CancelAsync(Guid tenantId, Guid saaSPaymentId, CancellationToken ct = default)
        {
            if (saaSPaymentId == raceOn)
                await race();
            return Result.Success();
        }
    }
}
