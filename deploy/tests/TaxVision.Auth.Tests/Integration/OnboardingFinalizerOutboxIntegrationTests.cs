extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using JasperFx;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Commands;
using Wolverine;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// BB-17. Host real y handler real, así que el finalizador se resuelve como en producción. Se mide en lo entregado por
/// una cola local durable. Sin reservas ni referido, FINALIZE no llama a Growth.
/// </summary>
public sealed class OnboardingFinalizerOutboxIntegrationTests : IAsyncLifetime
{
    private readonly DeliveredProbe _delivered = new();
    private readonly HandlerFailureSwitch _failure = new();
    private AuthTestDatabase _database = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        _factory = new AuthApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _database.ConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(_delivered);
                services.AddSingleton(_failure);
                services.ConfigureWolverine(options =>
                {
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                    options.Discovery.IncludeType(typeof(InvoiceRequestProbeHandler));
                    options.Policies.AddMiddleware(
                        typeof(FailAfterHandlerMiddleware),
                        chain => chain.MessageType == typeof(OnboardingFinalizeCommand)
                    );
                    options
                        .PublishMessage<OnboardingInvoiceRequestedIntegrationEvent>()
                        .ToLocalQueue("bb17-probe")
                        .UseDurableInbox();
                });
            });
        });
        _ = _factory.Services;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    private async Task InvokeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new OnboardingFinalizeCommand(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Starter plan",
                    "Payer",
                    null,
                    null,
                    GrossAmountCents: 0,
                    DiscountAmountCents: 0,
                    NetAmountCents: 0,
                    "USD",
                    "Free",
                    ReferralAttributionId: null,
                    []
                )
            );
    }

    [Fact(Timeout = 120_000)]
    public async Task Failure_after_finalizing_delivers_no_invoice_request()
    {
        _failure.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(InvokeAsync);
        await _delivered.SettleAsync(expected: 1);

        Assert.Empty(_delivered.Of<OnboardingInvoiceRequestedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Success_delivers_the_invoice_request()
    {
        await InvokeAsync();
        await _delivered.SettleAsync(expected: 1);

        Assert.Single(_delivered.Of<OnboardingInvoiceRequestedIntegrationEvent>());
    }

    public static class InvoiceRequestProbeHandler
    {
        public static void Handle(OnboardingInvoiceRequestedIntegrationEvent message, DeliveredProbe probe) =>
            probe.Add(message);
    }
}
