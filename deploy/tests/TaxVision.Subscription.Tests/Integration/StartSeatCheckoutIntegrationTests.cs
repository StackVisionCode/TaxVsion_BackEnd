using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Seats.Commands.StartSeatCheckout;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Tests.TestDoubles;
using Wolverine;
using Xunit;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// SUB-7 contra SQL real y con el middleware transaccional de Wolverine: la intención se confirma antes de llamar
/// a PaymentApp, y un fallo después de que PaymentApp aceptó reusa la misma intención y la misma clave.
/// </summary>
public sealed class StartSeatCheckoutIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();
    private readonly string _schema = SubscriptionWolverineHost.NewSchema("sub7");
    private readonly FailAttachInterceptor _failAttach = new();
    private readonly SessionClient _client = new();
    private readonly RecordingCancellationClient _cancellation = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await _db.SeedActiveSubscriptionAsync();
        _host = await SubscriptionWolverineHost.StartAsync(
            _schema,
            options =>
            {
                foreach (var handler in CheckoutHandlers())
                    options.Discovery.IncludeType(handler);
                options.Services.AddSingleton<ISeatCheckoutPaymentClient>(_client);
                options.Services.AddSingleton<IHostedCheckoutCancellationClient>(_cancellation);
                options.Services.AddSingleton<ISeatPricingRepository>(new FakeSeatPricingRepository(PricingUsd()));
                options.Services.AddScoped<ISeatPurchaseIntentRepository, SeatPurchaseIntentRepository>();
            },
            db => db.AddInterceptors(_failAttach)
        );
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _db.CleanupAsync();
        await SubscriptionWolverineHost.DropSchemaAsync(_schema);
    }

    [Fact(Timeout = 120_000)]
    public async Task FailureAfterProviderAccepted_DoesNotResend()
    {
        _failAttach.Armed = true;

        await Assert.ThrowsAnyAsync<Exception>(StartAsync);
        var retry = await StartAsync();

        Assert.True(retry.IsSuccess);
        Assert.Single(_client.Keys.Distinct());
        Assert.Equal(1, _client.SessionsCreated);
        var intent = Assert.Single(await IntentsAsync());
        Assert.Equal(retry.Value.SeatPurchaseIntentId, intent.Id);
        Assert.Equal(_client.SessionUrl, intent.CheckoutUrl);
    }

    [Fact(Timeout = 120_000)]
    public async Task ReservationIsVisibleToAnotherContextDuringTheProviderCall()
    {
        var visibleDuringCall = 0;
        _client.DuringCall = async () => visibleDuringCall = (await IntentsAsync()).Count;

        var result = await StartAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, visibleDuringCall);
    }

    // El usuario cancela mientras PaymentApp crea la sesión: esa sesión no puede quedar viva.
    [Fact(Timeout = 120_000)]
    public async Task CancelledDuringTheProviderCall_ClosesTheNewSession()
    {
        _client.DuringCall = async () =>
        {
            await using var db = _db.NewContext();
            var intent = await db.SeatPurchaseIntents.IgnoreQueryFilters().SingleAsync(i => i.TenantId == _db.TenantId);
            Assert.True(intent.Cancel(Guid.NewGuid(), DateTime.UtcNow, "closed").IsSuccess);
            await db.SaveChangesAsync();
        };

        var result = await StartAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(_client.PaymentId, await _cancellation.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    private Task<Result<StartSeatCheckoutResponse>> StartAsync() =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<StartSeatCheckoutResponse>>(
                new StartSeatCheckoutCommand(
                    _db.TenantId,
                    "Standard",
                    2,
                    AutoRenew: true,
                    "owner@acme.test",
                    "https://app/ok",
                    "https://app/cancel",
                    "Stripe",
                    "Card",
                    Guid.NewGuid()
                )
            );

    private async Task<List<SeatPurchaseIntent>> IntentsAsync()
    {
        await using var db = _db.NewContext();
        db.Database.SetCommandTimeout(5);
        return await db
            .SeatPurchaseIntents.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == _db.TenantId)
            .ToListAsync();
    }

    private static IEnumerable<Type> CheckoutHandlers() =>
        typeof(StartSeatCheckoutCommand)
            .Assembly.GetTypes()
            .Where(type =>
                type.Name
                    is "StartSeatCheckoutHandler"
                        or "ReserveSeatCheckoutHandler"
                        or "AttachSeatCheckoutHandler"
                        or "CancelHostedCheckoutHandler"
            );

    private static SeatPricing PricingUsd()
    {
        var pricing = SeatPricing.Seed(Guid.NewGuid(), DateTime.UtcNow);
        pricing.AddPriceTier(
            SeatPriceTier
                .Create(pricing.Id, SeatType.Standard, BillingCycle.Monthly, Money.Create(15m, "USD").Value)
                .Value
        );
        return pricing;
    }

    /// <summary>PaymentApp falso: honra la clave. La misma clave devuelve la misma sesión sin crear otra.</summary>
    private sealed class SessionClient : ISeatCheckoutPaymentClient
    {
        private readonly HashSet<string> _sessions = [];

        public Guid PaymentId { get; } = Guid.NewGuid();
        public string SessionUrl => "https://pay.example/session";
        public List<string> Keys { get; } = [];
        public int SessionsCreated => _sessions.Count;
        public Func<Task>? DuringCall { get; set; }

        public async Task<Result<SeatCheckoutClientResult>> CreateCheckoutAsync(
            SeatCheckoutClientRequest request,
            CancellationToken ct = default
        )
        {
            Keys.Add(request.IdempotencyKey);
            _sessions.Add(request.IdempotencyKey);
            if (DuringCall is { } duringCall)
                await duringCall();

            return Result.Success(
                new SeatCheckoutClientResult(PaymentId, SessionUrl, "cs_1", DateTime.UtcNow.AddHours(24))
            );
        }

        public Task<SeatPaymentStatusResult?> GetPaymentStatusAsync(
            Guid tenantId,
            Guid saaSPaymentId,
            CancellationToken ct = default
        ) => Task.FromResult<SeatPaymentStatusResult?>(null);
    }

    private sealed class RecordingCancellationClient : IHostedCheckoutCancellationClient
    {
        public TaskCompletionSource<Guid> Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Result> CancelAsync(Guid tenantId, Guid saaSPaymentId, CancellationToken ct = default)
        {
            Cancelled.TrySetResult(saaSPaymentId);
            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>Falla una vez el guardado que adjunta la sesión a la intención.</summary>
    private sealed class FailAttachInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var attaching = eventData
                .Context!.ChangeTracker.Entries<SeatPurchaseIntent>()
                .Any(entry => entry.Entity.CheckoutUrl is not null);
            if (!Armed || !attaching)
                return ValueTask.FromResult(result);

            Armed = false;
            throw new InvalidOperationException("Fallo forzado al adjuntar la sesión.");
        }
    }
}
