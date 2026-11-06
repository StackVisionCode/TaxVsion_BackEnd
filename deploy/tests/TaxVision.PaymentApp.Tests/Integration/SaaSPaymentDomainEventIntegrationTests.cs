using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcileStuckSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.DomainEventHandlers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentApp.Tests.TestDoubles;
using Wolverine;
using Wolverine.Transports.Tcp;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>
/// PAY-BB1 contra SQL real, con los repositorios reales: el evento de dominio se guarda en la transacción del
/// comando y su handler deja el aviso en el outbox. Los avisos van a un puerto TCP cerrado, así que quedan en
/// wolverine_outgoing_envelopes del esquema de la prueba. Solo filas del tenant de la prueba.
/// </summary>
public sealed class SaaSPaymentDomainEventIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _schema = PaymentAppWolverineHost.NewSchema("paybb1");
    private readonly FailAfterSaveInterceptor _failAfterSave = new();
    private IHost _host = null!;

    public async Task InitializeAsync() => _host = await StartHostAsync();

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await PaymentAppWolverineHost.ExecuteAsync(
            "DELETE a FROM dbo.SaaSPaymentAttempts a JOIN dbo.SaaSPayments p ON p.Id = a.SaaSPaymentId WHERE p.TenantId = @tenant; "
                + "DELETE FROM dbo.SaaSPayments WHERE TenantId = @tenant;",
            _tenantId
        );
        await PaymentAppWolverineHost.DropSchemaAsync(_schema);
    }

    [Fact(Timeout = 120_000)]
    public async Task Failed_payment_commits_its_event_and_the_handler_leaves_one_notice()
    {
        var payment = await SeedProcessingRenewalAsync();

        var result = await Bus.InvokeAsync<Result>(new ReconcileStuckSaaSPaymentCommand(_tenantId, payment.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, await StatusAsync(payment.Id));
        Assert.Equal(1, await WaitForOutgoingAsync(nameof(SubscriptionRenewalPaymentFailedIntegrationEvent)));
        Assert.Equal(1, await EnvelopesAsync("wolverine_incoming_envelopes", nameof(SaaSPaymentFailed)));
        Assert.Equal(0, await EnvelopesAsync("wolverine_dead_letters", nameof(SaaSPaymentFailed)));
    }

    [Fact(Timeout = 120_000)]
    public async Task Failure_after_save_leaves_neither_the_transition_nor_the_event()
    {
        var payment = await SeedProcessingRenewalAsync();
        _failAfterSave.Armed = true;

        var error = await Record.ExceptionAsync(() =>
            Bus.InvokeAsync<Result>(new ReconcileStuckSaaSPaymentCommand(_tenantId, payment.Id))
        );

        Assert.NotNull(error);
        Assert.Equal(PaymentStatus.Processing, await StatusAsync(payment.Id));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await EnvelopesAsync("wolverine_incoming_envelopes", nameof(SaaSPaymentFailed)));
        Assert.Equal(0, await OutgoingAsync(nameof(SubscriptionRenewalPaymentFailedIntegrationEvent)));
    }

    // Las dos formas sin aviso también pasan por la cola durable: se manejan y no dejan nada en el outbox.
    [Fact(Timeout = 120_000)]
    public async Task Owner_cancellation_and_review_events_are_handled_without_notices()
    {
        var payment = ProcessingRenewal();
        payment.MarkCancelled("Superseded.", DateTime.UtcNow);
        payment.FlagForReview(SaaSPayment.ReviewReasonLateProviderSuccess, "pi_late", 4900, "usd", DateTime.UtcNow);

        foreach (var domainEvent in payment.DomainEvents)
            await Bus.PublishAsync((object)domainEvent);

        Assert.Equal(1, await WaitForHandledAsync(nameof(SaaSPaymentCancelled)));
        Assert.Equal(1, await WaitForHandledAsync(nameof(SaaSPaymentReviewRequired)));
        Assert.Equal(0, await PaymentAppWolverineHost.CountRowsAsync(_schema, "wolverine_outgoing_envelopes"));
        Assert.Equal(0, await PaymentAppWolverineHost.CountRowsAsync(_schema, "wolverine_dead_letters"));
    }

    private IMessageBus Bus => _host.Services.GetRequiredService<IMessageBus>();

    private Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        var provider = new StatusPaymentProvider(
            Result.Success(
                new ChargeAuthorizationResult(
                    "pi_paybb1",
                    PaymentStatus.Failed,
                    FailureCode: "card_declined",
                    FailureMessage: "Declined."
                )
            )
        );
        return PaymentAppWolverineHost.StartAsync(
            _schema,
            options =>
            {
                options.Discovery.IncludeType(typeof(ReconcileStuckSaaSPaymentHandler));
                options.Discovery.IncludeType(typeof(SaaSPaymentDomainEventHandler));
                options.Services.AddScoped<ITenantRegistry, TenantRegistry>();
                options.Services.AddSingleton<IPaymentAdapterFactory>(new FakePaymentAdapterFactory(provider));
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);
                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.UseTaxVisionDurableLocalQueues();
                options
                    .PublishMessage<SubscriptionRenewalPaymentFailedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
            },
            db => db.AddInterceptors(_failAfterSave)
        );
    }

    private async Task<SaaSPayment> SeedProcessingRenewalAsync()
    {
        var payment = ProcessingRenewal();
        await using var db = NewContext();
        db.SaaSPayments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private SaaSPayment ProcessingRenewal()
    {
        var nowUtc = DateTime.UtcNow;
        var payment = SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"paybb1-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.PayPal,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                nowUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.PayPal, "pi_paybb1").Value,
            "processing",
            null,
            Guid.Empty,
            nowUtc
        );
        return payment;
    }

    private async Task<PaymentStatus> StatusAsync(Guid paymentId)
    {
        await using var db = NewContext();
        return (await db.SaaSPayments.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == paymentId)).Status;
    }

    private PaymentAppDbContext NewContext()
    {
        var tenant = new ReconcileStuckSaaSPaymentIntegrationTests.TestTenantContext();
        tenant.SetTenant(_tenantId);
        return new PaymentAppDbContext(
            new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(PaymentAppWolverineHost.Connection).Options,
            tenant
        );
    }

    private Task<int> OutgoingAsync(string messageType) => EnvelopesAsync("wolverine_outgoing_envelopes", messageType);

    private Task<int> EnvelopesAsync(string table, string messageType) =>
        PaymentAppWolverineHost.CountRowsAsync(_schema, table, $"message_type LIKE '%{messageType}%'");

    private Task<int> WaitForOutgoingAsync(string messageType) => WaitForAsync(() => OutgoingAsync(messageType));

    private Task<int> WaitForHandledAsync(string messageType) =>
        WaitForAsync(() =>
            PaymentAppWolverineHost.CountRowsAsync(
                _schema,
                "wolverine_incoming_envelopes",
                $"message_type LIKE '%{messageType}%' AND status = 'Handled'"
            )
        );

    private static async Task<int> WaitForAsync(Func<Task<int>> count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var current = await count();
            if (current > 0)
                return current;
            await Task.Delay(250);
        }
        return 0;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Falla una vez después de que el UPDATE llegó a la base y antes del commit del handler.</summary>
    private sealed class FailAfterSaveInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Armed)
                return ValueTask.FromResult(result);
            Armed = false;
            throw new InvalidOperationException("Fallo forzado después de guardar.");
        }
    }
}
