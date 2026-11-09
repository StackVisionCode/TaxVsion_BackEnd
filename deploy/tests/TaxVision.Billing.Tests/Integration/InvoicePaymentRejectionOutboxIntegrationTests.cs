extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using JasperFx;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Billing.Infrastructure.Persistence;
using Wolverine;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BB-17. <c>Rejection</c> recibe por constructor el bus que el consumer recibe por parámetro, no otro del contenedor:
/// host real, handler real y medido en lo entregado por una cola local durable. Un cobro con referencia inválida no
/// toca ninguna factura.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class InvoicePaymentRejectionOutboxIntegrationTests(BillingTestDatabase database) : IAsyncLifetime
{
    private readonly DeliveredProbe _delivered = new();
    private readonly HandlerFailureSwitch _failure = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new BillingApiFactory(database).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(_delivered);
                services.AddSingleton(_failure);
                services.ConfigureWolverine(options =>
                {
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                    options.Discovery.IncludeType(typeof(RejectionProbeHandler));
                    options.Policies.AddMiddleware(
                        typeof(FailAfterHandlerMiddleware),
                        chain => chain.MessageType == typeof(TenantPaymentSucceededIntegrationEvent)
                    );
                    options
                        .PublishMessage<InvoicePaymentRejectedIntegrationEvent>()
                        .ToLocalQueue("bb17-probe")
                        .UseDurableInbox();
                });
            })
        );
        _ = _factory.Services;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<BillingDbContext>()
                .UnappliedPayments.IgnoreQueryFilters()
                .Where(payment => payment.TenantId == _tenantId)
                .ExecuteDeleteAsync();
        await _factory.DisposeAsync();
    }

    private async Task InvokeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new TenantPaymentSucceededIntegrationEvent
                {
                    TenantId = _tenantId,
                    CorrelationId = "bb-17",
                    TenantPaymentId = Guid.NewGuid(),
                    ProviderCode = "stripe",
                    PurposeKind = "InvoicePayment",
                    ExternalReferenceId = "not-an-invoice",
                    AmountCents = 1_000,
                    Currency = "USD",
                    PaidAtUtc = DateTime.UtcNow,
                }
            );
    }

    private async Task<int> UnappliedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<BillingDbContext>()
            .UnappliedPayments.IgnoreQueryFilters()
            .CountAsync(payment => payment.TenantId == _tenantId);
    }

    [Fact(Timeout = 120_000)]
    public async Task Failure_after_rejecting_a_payment_delivers_nothing()
    {
        _failure.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(InvokeAsync);
        await _delivered.SettleAsync(expected: 1);

        Assert.Equal(0, await UnappliedAsync());
        Assert.Empty(_delivered.Of<InvoicePaymentRejectedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Success_delivers_the_rejection()
    {
        await InvokeAsync();
        await _delivered.SettleAsync(expected: 1);

        Assert.Equal(1, await UnappliedAsync());
        Assert.Single(_delivered.Of<InvoicePaymentRejectedIntegrationEvent>());
    }

    public static class RejectionProbeHandler
    {
        public static void Handle(InvoicePaymentRejectedIntegrationEvent message, DeliveredProbe probe) =>
            probe.Add(message);
    }
}
