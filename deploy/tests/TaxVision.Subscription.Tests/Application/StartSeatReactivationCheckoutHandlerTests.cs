using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Seats.Commands.StartSeatReactivationCheckout;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>SUB-7: el renewal pendiente se confirma antes de llamar a PaymentApp, con la clave estable del seat.</summary>
public sealed class StartSeatReactivationCheckoutHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Confirms_the_renewal_before_calling_the_provider()
    {
        var seat = SuspendedSeat();
        var unitOfWork = new FakeUnitOfWork();
        var client = new OrderCheckingClient(unitOfWork);

        var result = await StartAsync(seat, unitOfWork, client);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, client.SavesBeforeCall);
        Assert.StartsWith("seat-reactivation-", client.LastRequest!.IdempotencyKey);
        Assert.Single(seat.Renewals);
    }

    [Fact]
    public async Task A_retry_uses_the_same_key_and_the_same_renewal()
    {
        var seat = SuspendedSeat();
        var unitOfWork = new FakeUnitOfWork();
        var client = new OrderCheckingClient(unitOfWork);

        await StartAsync(seat, unitOfWork, client);
        var firstKey = client.LastRequest!.IdempotencyKey;
        await StartAsync(seat, unitOfWork, client);

        Assert.Equal(firstKey, client.LastRequest.IdempotencyKey);
        Assert.Single(seat.Renewals);
    }

    // SUB-14: empezar la reactivación toca UpdatedAtUtc; la clave no puede depender de eso.
    [Fact]
    public async Task Two_clicks_on_a_past_due_seat_open_one_session()
    {
        var seat = PastDueSeat();
        var unitOfWork = new FakeUnitOfWork();
        var client = new OrderCheckingClient(unitOfWork);

        await StartAsync(seat, unitOfWork, client);
        var firstKey = client.LastRequest!.IdempotencyKey;
        await Task.Delay(5);
        await StartAsync(seat, unitOfWork, client);

        Assert.Equal(firstKey, client.LastRequest.IdempotencyKey);
        Assert.Single(seat.Renewals);
    }

    // Pasado el plazo la sesión ya caducó: repetir la clave devolvería una URL muerta.
    [Fact]
    public async Task After_the_session_window_a_new_click_opens_another_session()
    {
        var seat = PastDueSeat();
        var unitOfWork = new FakeUnitOfWork();
        var client = new OrderCheckingClient(unitOfWork);

        await StartAsync(seat, unitOfWork, client, TimeSpan.Zero);
        var firstKey = client.LastRequest!.IdempotencyKey;
        await Task.Delay(5);
        await StartAsync(seat, unitOfWork, client, TimeSpan.Zero);

        Assert.NotEqual(firstKey, client.LastRequest.IdempotencyKey);
        Assert.Equal(2, seat.Renewals.Count);
    }

    // SUB-16: la clave de un asiento Suspended es fija; repetirla devolvía la URL de la sesión vencida.
    [Fact]
    public async Task Suspended_seat_after_the_session_window_opens_another_session()
    {
        var seat = SuspendedSeat();
        var unitOfWork = new FakeUnitOfWork();
        var client = new OrderCheckingClient(unitOfWork);

        await StartAsync(seat, unitOfWork, client, TimeSpan.Zero);
        var expiredKey = client.LastRequest!.IdempotencyKey;
        await Task.Delay(5);
        await StartAsync(seat, unitOfWork, client, TimeSpan.Zero);
        var secondKey = client.LastRequest.IdempotencyKey;
        await StartAsync(seat, unitOfWork, client);

        Assert.NotEqual(expiredKey, secondKey);
        Assert.StartsWith("seat-reactivation-", secondKey);
        Assert.Equal(secondKey, client.LastRequest.IdempotencyKey);
        Assert.Equal(2, seat.Renewals.Count);
    }

    private static Task<Result<StartSeatReactivationCheckoutResponse>> StartAsync(
        SubscriptionSeat seat,
        FakeUnitOfWork unitOfWork,
        ISeatReactivationCheckoutClient client,
        TimeSpan? sessionWindow = null
    )
    {
        var options = new SeatReactivationOptions();
        options.OpenSessionWindow = sessionWindow ?? options.OpenSessionWindow;
        var seats = new CapturingSeatRepo();
        seats.Added.Add(seat);
        var bus = new CapturingMessageBus
        {
            OnInvoke = async (message, token) =>
                await BeginSeatReactivationHandler.Handle(
                    (BeginSeatReactivationCommand)message,
                    seats,
                    unitOfWork,
                    Options.Create(options),
                    token
                ),
        };

        return StartSeatReactivationCheckoutHandler.Handle(
            new StartSeatReactivationCheckoutCommand(
                TenantId,
                seat.Id,
                "owner@acme.test",
                "https://app/ok",
                "https://app/cancel",
                "Stripe",
                "Card",
                Guid.NewGuid()
            ),
            bus,
            client,
            CancellationToken.None
        );
    }

    private static SubscriptionSeat SuspendedSeat()
    {
        var seat = PastDueSeat();
        seat.SuspendAfterRetriesExhausted(Guid.Empty, DateTime.UtcNow);
        return seat;
    }

    private static SubscriptionSeat PastDueSeat()
    {
        var nowUtc = DateTime.UtcNow;
        var seat = SubscriptionSeat
            .Purchase(
                TenantId,
                SeatType.Standard,
                SeatSourceType.Manual,
                null,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                nowUtc
            )
            .Value;
        seat.Activate(nowUtc.AddDays(-40), nowUtc.AddDays(-10), Guid.Empty, nowUtc);
        seat.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, nowUtc);
        return seat;
    }

    private sealed class OrderCheckingClient(FakeUnitOfWork unitOfWork) : ISeatReactivationCheckoutClient
    {
        public int SavesBeforeCall { get; private set; }
        public SeatReactivationCheckoutClientRequest? LastRequest { get; private set; }

        public Task<Result<SeatCheckoutClientResult>> CreateReactivationCheckoutAsync(
            SeatReactivationCheckoutClientRequest request,
            CancellationToken ct = default
        )
        {
            SavesBeforeCall = unitOfWork.SaveChangesCallCount;
            LastRequest = request;
            return Task.FromResult(
                Result.Success(
                    new SeatCheckoutClientResult(Guid.NewGuid(), "https://pay/x", "cs_1", DateTime.UtcNow.AddHours(24))
                )
            );
        }
    }
}
