using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using JasperFx.CodeGeneration.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Wallet.Application.Wallet;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Application.Wallet.Commands;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;

namespace TaxVision.Wallet.Tests.Application;

/// <summary>WAL-7: el flujo del controller (orden → checkout → cierre si falla) con Wolverine real y repositorios
/// en memoria, sin SQL.</summary>
public sealed class StartTopUpCheckoutFlowTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeWalletRepository _wallets = new();
    private readonly FakeWalletTopUpCheckoutClient _checkout = new();
    private IHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IWalletRepository>(_wallets);
                services.AddSingleton<IWalletTopUpCheckoutClient>(_checkout);
                services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
                services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
            })
            .UseWolverine(options =>
            {
                options.Durability.Mode = DurabilityMode.MediatorOnly;
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(CreateTopUpOrderHandler));
                options.Discovery.IncludeType(typeof(StartTopUpCheckoutHandler));
                options.Discovery.IncludeType(typeof(MarkTopUpCheckoutFailedHandler));
            })
            .StartAsync();

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task CheckoutFails_MarksOrderFailed()
    {
        _checkout.NextFailure = new Error("WalletTopUp.Checkout.Unavailable", "The payment service is unavailable.");

        var result = await RunAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("WalletTopUp.Checkout.Unavailable", result.Error.Code);
        var order = Assert.Single(_wallets.TopUps);
        Assert.Equal(WalletTopUpStatus.Failed, order.Status);
        Assert.Equal(TopUpCheckoutFlow.CheckoutUnavailableReason, order.FailureReason);
    }

    [Fact]
    public async Task CheckoutSucceeds_ReturnsUrlAndKeepsOrderPending()
    {
        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("https://checkout.test/session", result.Value.CheckoutUrl);
        var order = Assert.Single(_wallets.TopUps);
        Assert.Equal(WalletTopUpStatus.Pending, order.Status);
        Assert.Equal(order.IdempotencyKey, Assert.Single(_checkout.Requests).IdempotencyKey);
    }

    [Fact]
    public async Task InvalidOrder_DoesNotCallPaymentApp()
    {
        var result = await RunAsync(currency: "MXN");

        Assert.True(result.IsFailure);
        Assert.Equal(WalletErrors.CurrencyMismatch, result.Error);
        Assert.Empty(_wallets.TopUps);
        Assert.Equal(0, _checkout.Calls);
    }

    [Fact]
    public async Task OrderNoLongerPending_DoesNotCallPaymentApp()
    {
        var bus = _host.Services.GetRequiredService<IMessageBus>();
        var order = WalletTopUp.Create(_tenantId, 10_000, "USD", Guid.NewGuid()).Value;
        order.MarkFailed("declined");
        await _wallets.AddTopUpAsync(order);

        var result = await bus.InvokeAsync<Result<WalletTopUpCheckoutClientResult>>(
            new StartTopUpCheckoutCommand(
                _tenantId,
                order.Id,
                "payer@test.local",
                "https://app.test/ok",
                "https://app.test/cancel",
                "Stripe",
                "Card"
            )
        );

        Assert.True(result.IsFailure);
        Assert.Equal(WalletErrors.TopUpNotPending, result.Error);
        Assert.Equal(0, _checkout.Calls);
    }

    private Task<Result<TopUpCheckoutView>> RunAsync(string currency = "USD") =>
        TopUpCheckoutFlow.RunAsync(
            _host.Services.GetRequiredService<IMessageBus>(),
            new TopUpCheckoutRequest(
                _tenantId,
                Guid.NewGuid(),
                10_000,
                currency,
                "payer@test.local",
                "https://app.test/ok",
                "https://app.test/cancel",
                "Stripe",
                "Card"
            ),
            CancellationToken.None
        );
}
