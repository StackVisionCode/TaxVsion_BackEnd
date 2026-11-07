using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ClaimSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcilePendingSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RecordSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.DomainEventHandlers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentApp.Tests.TestDoubles;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Transports.Tcp;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>
/// PAY-5 (patrón BB-8) contra SQL real con Wolverine real: el comando de cobro corre sin transacción, un fallo después
/// de que el proveedor aceptó no crea otro cargo y lo que quedó sin resultado se recupera. Los avisos van a un puerto
/// cerrado, así que se cuentan en wolverine_outgoing_envelopes del esquema de la prueba.
/// </summary>
public sealed class ExecuteSaaSPaymentChargeIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _schema = PaymentAppWolverineHost.NewSchema("pay5");
    private readonly FailOnSucceededInterceptor _interceptor = new();
    private readonly IdempotentCharger _charger = new();
    private readonly int _closedPort = FreeTcpPort();
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
    public async Task Charge_runs_outside_a_transaction_with_the_row_already_committed()
    {
        var payment = await SeedPendingAsync();
        var visibleDuringCall = false;
        _charger.OnCharge = async () =>
        {
            await using var db = NewContext();
            visibleDuringCall = await db
                .SaaSPayments.IgnoreQueryFilters()
                .AnyAsync(p => p.Id == payment.Id && p.ChargeAttemptedAtUtc != null);
        };

        await Bus.InvokeAsync(Execute(payment));

        Assert.True(visibleDuringCall, "El intento tiene que estar grabado antes de llamar al proveedor.");
        Assert.Equal(PaymentStatus.Succeeded, await StatusAsync(payment.Id));
    }

    [Fact(Timeout = 120_000)]
    public async Task FailureAfterProviderAccepted_DoesNotResend()
    {
        var payment = await SeedPendingAsync();
        _interceptor.FailuresLeft = 1;

        await Bus.InvokeAsync(Execute(payment));

        Assert.Equal(1, _charger.EffectiveCharges);
        Assert.Equal(PaymentStatus.Succeeded, await StatusAsync(payment.Id));
        Assert.Equal(1, await WaitForOutgoingAsync(nameof(SubscriptionRenewalPaymentSucceededIntegrationEvent)));
    }

    // El registro falla todos sus reintentos: el comando entero se repite y llega al proveedor con la misma clave.
    [Fact(Timeout = 120_000)]
    public async Task Repeated_command_after_a_failed_record_sends_the_same_key()
    {
        var payment = await SeedPendingAsync();
        _interceptor.FailuresLeft = RecordAttempts;

        await Bus.InvokeAsync(Execute(payment));

        Assert.True(_charger.Calls >= 2, "El comando tenía que repetirse.");
        Assert.Equal(1, _charger.EffectiveCharges);
        Assert.Single(_charger.Keys);
        Assert.Equal(PaymentStatus.Succeeded, await StatusAsync(payment.Id));
        Assert.Equal(1, await WaitForOutgoingAsync(nameof(SubscriptionRenewalPaymentSucceededIntegrationEvent)));
    }

    // El caso que preocupa: el proveedor cobró, nada se pudo guardar y el comando se agotó.
    [Fact(Timeout = 120_000)]
    public async Task ChargedButUnrecorded_IsRecoveredByReconciliation()
    {
        var payment = await SeedPendingAsync();
        _interceptor.FailuresLeft = int.MaxValue;

        await Assert.ThrowsAnyAsync<Exception>(() => Bus.InvokeAsync(Execute(payment)));

        Assert.Equal(1, _charger.EffectiveCharges);
        Assert.Equal(PaymentStatus.Pending, await StatusAsync(payment.Id));

        _interceptor.FailuresLeft = 0;
        await AgeAsync(payment.Id);
        _charger.Provider.Lookup = new ChargeLookup(
            ChargeLookupOutcome.Found,
            new ChargeAuthorizationResult("pi_pay5_recovered", PaymentStatus.Succeeded)
        );
        await Bus.InvokeAsync<Result>(new ReconcilePendingSaaSPaymentCommand(_tenantId, payment.Id));

        Assert.Equal(PaymentStatus.Succeeded, await StatusAsync(payment.Id));
        Assert.Equal(1, _charger.EffectiveCharges);
        Assert.Equal(1, await WaitForOutgoingAsync(nameof(SubscriptionRenewalPaymentSucceededIntegrationEvent)));
    }

    [Fact(Timeout = 120_000)]
    public async Task UnknownOutcome_ProviderCannotSearch_GoesToReviewAndIsNotCharged()
    {
        var payment = await SeedPendingAsync();
        _interceptor.FailuresLeft = int.MaxValue;
        await Assert.ThrowsAnyAsync<Exception>(() => Bus.InvokeAsync(Execute(payment)));
        _interceptor.FailuresLeft = 0;
        await AgeAsync(payment.Id);
        _charger.Provider.Lookup = new ChargeLookup(ChargeLookupOutcome.NotSupported);
        var calls = _charger.Calls;

        await Bus.InvokeAsync<Result>(new ReconcilePendingSaaSPaymentCommand(_tenantId, payment.Id));
        await Bus.InvokeAsync(Execute(payment));

        Assert.Equal(calls, _charger.Calls);
        await using var db = NewContext();
        var stored = await db.SaaSPayments.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Pending, stored.Status);
        Assert.Equal(SaaSPayment.ReviewReasonChargeOutcomeUnknown, stored.ReviewReasonCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Crash_before_charge_leaves_pending_row_visible_to_reconciliation()
    {
        await _host.StopAsync();
        _host.Dispose();
        _host = null!;

        // Se guarda la intención y su comando juntos, y el host se detiene antes de procesarlo.
        var payment = await SeedPendingAsync();
        _charger.Hold = true;
        using (var first = await StartHostAsync())
        {
            await first.Services.GetRequiredService<IMessageBus>().PublishAsync(Execute(payment));
            await _charger.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await first.StopAsync(stopTimeout.Token);
        }

        Assert.Equal(PaymentStatus.Pending, await StatusAsync(payment.Id));
        await using (var db = NewContext())
        {
            var repository = new SaaSPaymentRepository(db);
            var stale = await repository.GetStalePendingChargeIdsAsync(DateTime.UtcNow.AddMinutes(1), 500);
            Assert.Contains(stale, row => row.Id == payment.Id);
        }

        // Al volver, el comando durable se procesa y el cobro se completa una sola vez.
        _charger.Hold = false;
        _host = await StartHostAsync();
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (await StatusAsync(payment.Id) != PaymentStatus.Succeeded && DateTime.UtcNow < deadline)
            await Task.Delay(200);

        Assert.Equal(PaymentStatus.Succeeded, await StatusAsync(payment.Id));
        Assert.Equal(1, _charger.EffectiveCharges);
    }

    // Después de un corte, el job y el comando pueden correr a la vez: el intento grabado decide quién sigue.
    [Fact(Timeout = 120_000)]
    public async Task Reconciliation_does_not_close_a_payment_whose_charge_just_started()
    {
        var payment = await SeedPendingAsync();
        await AgeAsync(payment.Id);
        _charger.OnCharge = () =>
            Bus.InvokeAsync<Result>(new ReconcilePendingSaaSPaymentCommand(_tenantId, payment.Id));

        await Bus.InvokeAsync(Execute(payment));

        Assert.Equal(PaymentStatus.Succeeded, await StatusAsync(payment.Id));
        Assert.Equal(1, _charger.EffectiveCharges);
    }

    private const int RecordAttempts = 3;

    private IMessageBus Bus => _host.Services.GetRequiredService<IMessageBus>();

    private ExecuteSaaSPaymentChargeCommand Execute(SaaSPayment payment) =>
        new(_tenantId, payment.Id, SaaSPaymentChargeStrategy.OffSession, "pm_card", null, Guid.Empty, null);

    private async Task<SaaSPayment> SeedPendingAsync()
    {
        var payment = SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"pay5-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        await using var db = NewContext();
        db.SaaSPayments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    // La reconciliación solo toma pagos sin cambios desde hace rato.
    private Task AgeAsync(Guid paymentId) =>
        PaymentAppWolverineHost.ExecuteAsync(
            $"UPDATE dbo.SaaSPayments SET UpdatedAtUtc = DATEADD(hour, -1, UpdatedAtUtc) WHERE Id = '{paymentId}' AND TenantId = @tenant",
            _tenantId
        );

    private async Task<PaymentStatus> StatusAsync(Guid paymentId)
    {
        await using var db = NewContext();
        return await db
            .SaaSPayments.IgnoreQueryFilters()
            .Where(p => p.Id == paymentId)
            .Select(p => p.Status)
            .SingleAsync();
    }

    private async Task<int> WaitForOutgoingAsync(string messageType)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var count = 0;
        while (DateTime.UtcNow < deadline)
        {
            count = await PaymentAppWolverineHost.CountRowsAsync(
                _schema,
                "wolverine_outgoing_envelopes",
                $"message_type LIKE '%{messageType}%'"
            );
            if (count > 0)
                break;
            await Task.Delay(200);
        }

        // Margen para que un aviso duplicado, si existiera, también aparezca.
        await Task.Delay(TimeSpan.FromSeconds(1));
        return await PaymentAppWolverineHost.CountRowsAsync(
            _schema,
            "wolverine_outgoing_envelopes",
            $"message_type LIKE '%{messageType}%'"
        );
    }

    private Task<IHost> StartHostAsync() =>
        PaymentAppWolverineHost.StartAsync(
            _schema,
            options =>
            {
                options.Discovery.IncludeType(typeof(ExecuteSaaSPaymentChargeHandler));
                options.Discovery.IncludeType(typeof(ClaimSaaSPaymentChargeHandler));
                options.Discovery.IncludeType(typeof(RecordSaaSPaymentChargeHandler));
                options.Discovery.IncludeType(typeof(ReconcilePendingSaaSPaymentHandler));
                options.Discovery.IncludeType(typeof(SaaSPaymentDomainEventHandler));
                options.Services.AddScoped<ITenantRegistry, TenantRegistry>();
                options.Services.AddSingleton<IPaymentAdapterFactory>(new FakePaymentAdapterFactory(_charger.Provider));
                options.Services.AddSingleton<IOffSessionChargerFactory>(new SingleChargerFactory(_charger));
                options.Services.AddSingleton<IPaymentAuditLogWriter>(new FakePaymentAuditLogWriter());
                options.Services.AddSingleton<IPaymentAppMetrics>(new FakePaymentAppMetrics());
                options.Services.AddOptions<SaaSPaymentChargeOptions>();
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                // Misma forma que la política estándar, con esperas cortas.
                options
                    .Policies.OnException<Exception>()
                    .RetryWithCooldown(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20));

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.LocalQueue("payment-app-provider-calls").UseDurableInbox();
                options.PublishMessage<ExecuteSaaSPaymentChargeCommand>().ToLocalQueue("payment-app-provider-calls");
                options.UseTaxVisionDurableLocalQueues();
                options
                    .PublishMessage<SubscriptionRenewalPaymentSucceededIntegrationEvent>()
                    .ToPort(_closedPort)
                    .UseDurableOutbox();
                options.PublishMessage<SaaSPaymentSucceededIntegrationEvent>().ToPort(_closedPort).UseDurableOutbox();
                options
                    .PublishMessage<BuildingBlocks.Messaging.SubscriptionIntegrationEvents.TenantSubscriptionStatusChangedIntegrationEvent>()
                    .ToPort(_closedPort)
                    .UseDurableOutbox();
            },
            db => db.AddInterceptors(_interceptor)
        );

    private PaymentAppDbContext NewContext()
    {
        var tenant = new ReconcileStuckSaaSPaymentIntegrationTests.TestTenantContext();
        tenant.SetTenant(_tenantId);
        return new PaymentAppDbContext(
            new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(PaymentAppWolverineHost.Connection).Options,
            tenant
        );
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class SingleChargerFactory(IOffSessionCharger charger) : IOffSessionChargerFactory
    {
        public IOffSessionCharger? For(string providerCode) => charger;
    }

    // Honra la idempotencia como Stripe: la misma clave devuelve el mismo cargo y no cobra de nuevo.
    private sealed class IdempotentCharger : IOffSessionCharger
    {
        private readonly ConcurrentDictionary<string, string> _charges = new();
        private int _calls;

        public string ProviderCode => PaymentProviderCode.Stripe.ToString();
        public int Calls => _calls;
        public int EffectiveCharges => _charges.Count;
        public IEnumerable<string> Keys => _charges.Keys;
        public Func<Task>? OnCharge { get; set; }
        public bool Hold { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakePaymentProvider Provider { get; } = new();

        public async Task<Result<OffSessionChargeOutcome>> ChargeAsync(
            OffSessionChargeRequest request,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref _calls);
            if (Hold)
            {
                Reached.TrySetResult();
                await Task.Delay(Timeout.Infinite, CancellationToken.None);
            }

            if (OnCharge is { } onCharge)
                await onCharge();

            var reference = _charges.GetOrAdd(request.IdempotencyKey, _ => $"pi_pay5_{Guid.NewGuid():N}");
            return Result.Success(new OffSessionChargeOutcome(OffSessionChargeStatus.Succeeded, reference, null, null));
        }
    }

    // Falla después de guardar el éxito y antes de confirmar: el proveedor ya aceptó y la transacción se revierte.
    private sealed class FailOnSucceededInterceptor : SaveChangesInterceptor
    {
        private int _failuresLeft;

        public int FailuresLeft
        {
            get => _failuresLeft;
            set => _failuresLeft = value;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            var hit =
                _failuresLeft > 0
                && eventData
                    .Context!.ChangeTracker.Entries<SaaSPayment>()
                    .Any(e => e.Entity.Status == PaymentStatus.Succeeded);
            if (!hit)
                return ValueTask.FromResult(result);

            if (_failuresLeft != int.MaxValue)
                Interlocked.Decrement(ref _failuresLeft);
            return ValueTask.FromException<int>(new InvalidOperationException("Fallo forzado después de guardar."));
        }
    }
}
