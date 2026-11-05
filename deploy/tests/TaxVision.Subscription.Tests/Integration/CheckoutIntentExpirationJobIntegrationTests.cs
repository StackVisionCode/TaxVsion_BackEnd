using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
                1500,
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
                2900,
                Guid.NewGuid(),
                created
            )
            .Value;
        addOn.AttachCheckout(Guid.NewGuid(), "https://checkout.test/addon", expiredAt, created);
        var renewal = SubscriptionRenewalIntent
            .Create(_db.TenantId, 4900, "USD", BillingCycle.Monthly, Guid.NewGuid(), created)
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
        services.AddSingleton<IMessageBus>(new CapturingMessageBus());
        await using var provider = services.BuildServiceProvider();
        var job = new CheckoutIntentExpirationJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            null!,
            NullLogger<CheckoutIntentExpirationJob>.Instance
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
