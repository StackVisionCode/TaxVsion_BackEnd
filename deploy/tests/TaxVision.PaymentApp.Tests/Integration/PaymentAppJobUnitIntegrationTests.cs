using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Scheduling;
using TaxVision.PaymentApp.Tests.TestDoubles;
using Wolverine;
using Wolverine.Transports.Tcp;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>
/// PAY-BB3 contra SQL real: cada ítem de los jobs es una unidad transaccional. La marca y su aviso se confirman
/// juntos o no se confirma nada. Los avisos van a un puerto TCP cerrado, así que lo confirmado queda en
/// wolverine_outgoing_envelopes del esquema de la prueba. Los ítems se pasan por id: la prueba nunca recorre las
/// filas de otros tenants. La tarjeta sembrada vence en dos años, así que el barrido de la flota (30 días) no la toma.
/// </summary>
public sealed class PaymentAppJobUnitIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _schema = PaymentAppWolverineHost.NewSchema("paybb3");
    private readonly FailAfterSaveInterceptor _failAfterSave = new();
    private IHost _host = null!;

    // La prueba pasa un corte lejano: la tarjeta sembrada "vence" dentro de él.
    private static readonly DateTime FarCutoffUtc = DateTime.UtcNow.AddYears(3);

    public async Task InitializeAsync() => _host = await StartHostAsync();

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await PaymentAppWolverineHost.ExecuteAsync(
            "DELETE m FROM dbo.TenantSavedPaymentMethods m JOIN dbo.TenantProviderCustomers c ON c.Id = m.TenantProviderCustomerId WHERE c.TenantId = @tenant; "
                + "DELETE FROM dbo.TenantProviderCustomers WHERE TenantId = @tenant;",
            _tenantId
        );
        await PaymentAppWolverineHost.DropSchemaAsync(_schema);
    }

    [Fact(Timeout = 120_000)]
    public async Task ExpiringPaymentMethods_FailureAfterSave_rolls_back_notice_and_message()
    {
        var (customerId, methodId) = await SeedCustomerWithCardAsync();
        _failAfterSave.Armed = true;

        var summary = await NewExpiringJob().NotifyAsync([(customerId, _tenantId)], FarCutoffUtc, "test", default);

        Assert.Equal(1, summary.Failed);
        Assert.Null(await ExpiryNoticeSentAtAsync(methodId));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await OutgoingAsync(nameof(SaaSPaymentMethodExpiringSoonIntegrationEvent)));
    }

    [Fact(Timeout = 120_000)]
    public async Task ExpiringPaymentMethods_Committed_leaves_notice_and_envelope_together()
    {
        var (customerId, methodId) = await SeedCustomerWithCardAsync();

        var summary = await NewExpiringJob().NotifyAsync([(customerId, _tenantId)], FarCutoffUtc, "test", default);

        Assert.Equal(1, summary.Committed);
        Assert.NotNull(await ExpiryNoticeSentAtAsync(methodId));
        Assert.Equal(1, await OutgoingAsync(nameof(SaaSPaymentMethodExpiringSoonIntegrationEvent)));

        // Otra pasada no repite el aviso: la marca ya quedó guardada.
        var again = await NewExpiringJob().NotifyAsync([(customerId, _tenantId)], FarCutoffUtc, "test", default);
        Assert.Equal(1, again.Skipped);
        Assert.Equal(1, await OutgoingAsync(nameof(SaaSPaymentMethodExpiringSoonIntegrationEvent)));
    }

    // El backfill no guarda nada en el pago: el pedido de recibo sale por el outbox de su unidad.
    [Fact(Timeout = 120_000)]
    public async Task MissingReceiptBackfill_request_goes_through_the_unit_outbox()
    {
        var payment = SucceededPayment();

        var summary = await NewBackfillJob().RequestAsync([payment.Snapshot()], default);

        Assert.Equal(1, summary.Committed);
        Assert.Equal(1, await OutgoingAsync(nameof(SaaSPaymentSucceededIntegrationEvent)));
    }

    private ExpiringPaymentMethodsJob NewExpiringJob() =>
        new(ScopeFactory, lockFactory: null!, NullLogger<ExpiringPaymentMethodsJob>.Instance);

    private MissingReceiptBackfillJob NewBackfillJob() =>
        new(ScopeFactory, lockFactory: null!, NullLogger<MissingReceiptBackfillJob>.Instance);

    private IServiceScopeFactory ScopeFactory => _host.Services.GetRequiredService<IServiceScopeFactory>();

    private Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return PaymentAppWolverineHost.StartAsync(
            _schema,
            options =>
            {
                options.Services.AddSingleton<ITenantRegistry>(new FakeTenantRegistry("Acme Tax"));
                options
                    .PublishMessage<SaaSPaymentMethodExpiringSoonIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
                options.PublishMessage<SaaSPaymentSucceededIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            },
            db => db.AddInterceptors(_failAfterSave)
        );
    }

    private async Task<(Guid CustomerId, Guid MethodId)> SeedCustomerWithCardAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var reference = ProviderCustomerReference
            .Create(PaymentProviderCode.Stripe, $"cus_paybb3_{Guid.NewGuid():N}")
            .Value;
        var customer = TenantProviderCustomer
            .Register(_tenantId, PaymentProviderCode.Stripe, reference, "paybb3@example.test", nowUtc)
            .Value;
        var expiry = nowUtc.AddYears(2);
        var methodId = customer
            .AttachPaymentMethod(
                $"pm_paybb3_{Guid.NewGuid():N}",
                "visa",
                "4242",
                expiry.Month,
                expiry.Year,
                true,
                nowUtc
            )
            .Value;

        await using var db = NewContext();
        db.TenantProviderCustomers.Add(customer);
        await db.SaveChangesAsync();
        return (customer.Id, methodId);
    }

    private SaaSPayment SucceededPayment()
    {
        var nowUtc = DateTime.UtcNow;
        var payment = SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"paybb3-{Guid.NewGuid():N}").Value,
                Money.Create(1500, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                nowUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_paybb3").Value,
            "processing",
            null,
            Guid.Empty,
            nowUtc
        );
        payment.MarkSucceeded(nowUtc, Guid.Empty);
        return payment;
    }

    private async Task<DateTime?> ExpiryNoticeSentAtAsync(Guid methodId)
    {
        await using var db = NewContext();
        var customer = await db
            .TenantProviderCustomers.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(c => c.SavedMethods)
            .SingleAsync(c => c.TenantId == _tenantId);
        return customer.SavedMethods.Single(m => m.Id == methodId).ExpiryNoticeSentAtUtc;
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

    private Task<int> OutgoingAsync(string messageType) =>
        PaymentAppWolverineHost.CountRowsAsync(
            _schema,
            "wolverine_outgoing_envelopes",
            $"message_type LIKE '%{messageType}%'"
        );

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Falla una vez después de que el UPDATE llegó a la base y antes del commit de la unidad.</summary>
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
