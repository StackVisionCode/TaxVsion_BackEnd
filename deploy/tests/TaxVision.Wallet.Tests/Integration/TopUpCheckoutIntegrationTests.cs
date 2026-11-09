using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Wallet.Application.Wallet;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Application.Wallet.Commands;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>WAL-7 (patrón BB-8) contra el SQL Server local: la orden ya está confirmada cuando se llama a
/// PaymentApp, y un fallo después de que PaymentApp aceptó nunca la borra.</summary>
public sealed class TopUpCheckoutIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _schema = WalletIntegrationHost.NewSchema();
    private readonly ThrowAfterAcceptCheckoutClient _checkout;
    private IHost _host = null!;

    public TopUpCheckoutIntegrationTests() => _checkout = new ThrowAfterAcceptCheckoutClient(_tenantId);

    public async Task InitializeAsync() =>
        _host = await WalletIntegrationHost.StartAsync(
            _schema,
            _tenantId,
            [
                typeof(CreateTopUpOrderHandler),
                typeof(StartTopUpCheckoutHandler),
                typeof(MarkTopUpCheckoutFailedHandler),
            ],
            services => services.AddSingleton<IWalletTopUpCheckoutClient>(_checkout)
        );

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        await using (var db = WalletTestDatabase.NewContext(_tenantId))
            await db.WalletTopUps.IgnoreQueryFilters().Where(t => t.TenantId == _tenantId).ExecuteDeleteAsync();

        await WalletIntegrationHost.DropSchemaAsync(_schema);
    }

    [Fact]
    public async Task OrderIsCommittedBeforeCheckoutCall()
    {
        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.True(
            _checkout.OrderVisibleDuringCall,
            "La orden tiene que estar confirmada antes de llamar a PaymentApp."
        );
    }

    [Fact]
    public async Task FailureAfterProviderAccepted_DoesNotResend()
    {
        _checkout.ThrowAfterAccepting = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync());

        // La excepción no revirtió la orden: queda visible y cerrada, sin otra llamada a PaymentApp.
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        var order = await db.WalletTopUps.AsNoTracking().SingleAsync(t => t.TenantId == _tenantId);
        Assert.Equal(WalletTopUpStatus.Failed, order.Status);
        Assert.Equal(1, _checkout.Calls);
    }

    [Fact]
    public async Task RetryOfTheSameOrder_ReusesTheIdempotencyKey()
    {
        var bus = _host.Services.GetRequiredService<IMessageBus>();
        var order = await bus.InvokeAsync<Result<TopUpView>>(
            new CreateTopUpOrderCommand(_tenantId, Guid.NewGuid(), 10_000, "USD")
        );
        var start = new StartTopUpCheckoutCommand(
            _tenantId,
            order.Value.Id,
            "payer@test.local",
            "https://app.test/ok",
            "https://app.test/cancel",
            "Stripe",
            "Card"
        );

        await bus.InvokeAsync<Result<WalletTopUpCheckoutClientResult>>(start);
        await bus.InvokeAsync<Result<WalletTopUpCheckoutClientResult>>(start);

        Assert.Equal(2, _checkout.Calls);
        Assert.Single(_checkout.Keys.Distinct());
    }

    private Task<Result<TopUpCheckoutView>> RunAsync() =>
        TopUpCheckoutFlow.RunAsync(
            _host.Services.GetRequiredService<IMessageBus>(),
            new TopUpCheckoutRequest(
                _tenantId,
                Guid.NewGuid(),
                10_000,
                "USD",
                "payer@test.local",
                "https://app.test/ok",
                "https://app.test/cancel",
                "Stripe",
                "Card"
            ),
            CancellationToken.None
        );

    // Acepta y cuenta; desde otro contexto mira si la orden ya es visible.
    private sealed class ThrowAfterAcceptCheckoutClient(Guid tenantId) : IWalletTopUpCheckoutClient
    {
        private readonly FakeWalletTopUpCheckoutClient _inner = new();

        public bool ThrowAfterAccepting { get; set; }
        public bool OrderVisibleDuringCall { get; private set; }
        public int Calls => _inner.Calls;
        public IEnumerable<string> Keys => _inner.Requests.Select(r => r.IdempotencyKey);

        public async Task<Result<WalletTopUpCheckoutClientResult>> CreateCheckoutAsync(
            WalletTopUpCheckoutClientRequest request,
            CancellationToken ct = default
        )
        {
            await using (var db = WalletTestDatabase.NewContext(tenantId))
                OrderVisibleDuringCall = await db
                    .WalletTopUps.AsNoTracking()
                    .AnyAsync(t => t.Id == request.TopUpId, ct);

            var accepted = await _inner.CreateCheckoutAsync(request, ct);
            if (ThrowAfterAccepting)
                throw new InvalidOperationException("Fallo después de que PaymentApp aceptó.");
            return accepted;
        }
    }
}
