using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>
/// Verifica que un cobro de monto cero (p.ej. un código de descuento al 100%) nunca toca al
/// payment provider — ni en modo DirectApiKeys ni en modo Connect. Los fakes de
/// <see cref="IPaymentAdapterFactory"/> y <see cref="ITenantConnectAccountRepository"/> lanzan
/// si se los invoca, así que el test falla ruidosamente si el short-circuit de monto cero deja
/// de funcionar.
/// </summary>
public sealed class ChargeTenantPaymentHandlerTests
{
    [Fact]
    public async Task Zero_amount_charge_in_Connect_mode_never_touches_the_provider_or_the_connect_account()
    {
        var tenantId = Guid.NewGuid();
        var config = TenantPaymentConfig
            .Create(
                tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.Connect,
                "pk_test_123",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                DateTime.UtcNow
            )
            .Value;
        config.MarkActiveViaConnect(Guid.Empty, DateTime.UtcNow);

        var payments = new FakeTenantPaymentRepository();
        var configs = new FakeTenantPaymentConfigRepository(config);
        // Lanza si algo intenta resolver la Connect account — no debería pasar para $0.
        var connectAccounts = new ThrowingTenantConnectAccountRepository();
        // Lanza si algo intenta resolver un adapter de provider — no debería pasar para $0.
        var providerFactory = new ThrowingPaymentAdapterFactory();
        var secretProtector = new ThrowingSecretProtector();
        var audit = new FakePaymentAuditLogWriter();
        var metrics = new FakePaymentClientMetrics();

        var command = new ChargeTenantPaymentCommand(
            tenantId,
            PaymentProviderCode.Stripe,
            AmountCents: 0,
            Currency: "USD",
            TaxpayerId: Guid.NewGuid(),
            PaymentPurposeKind.InvoicePayment,
            PurposeExternalReferenceId: "inv-100pct-off",
            PaymentMethodReference: "pm_unused",
            ReceiptEmail: null,
            IdempotencyKey: "charge-key-1",
            ActorUserId: Guid.Empty,
            // Null sería inválido para un cobro Connect real (feeCents requerido) — probamos
            // que igual funciona porque la rama de Connect nunca se ejecuta para $0.
            PlatformFeeAmountCents: null,
            PlatformFeeReference: null
        );

        var result = await ChargeTenantPaymentHandler.Handle(
            command,
            payments,
            configs,
            connectAccounts,
            providerFactory,
            secretProtector,
            Options.Create(
                new PlatformStripeCredentials { PlatformSecretKey = "sk_platform", ConnectWebhookSecret = "whsec" }
            ),
            audit,
            new FakeUnitOfWork(),
            InMemoryPaymentClient.RecordingBus([]),
            metrics,
            new FakeCorrelationContext(),
            NoOpLogger<TenantPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var payment = Assert.Single(payments.Added);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(0, payment.Amount.AmountCents);
        Assert.Null(payment.SplitPayment);
        Assert.Null(payment.ProviderChargeReferenceOnConnect);
        Assert.Null(payment.ExternalChargeReference);
        Assert.Empty(payment.Attempts);

        Assert.Equal((0L, "USD"), metrics.PaymentsSucceeded.Single());
        Assert.Empty(metrics.PlatformFees);
    }

    [Fact]
    public async Task Zero_amount_charge_in_DirectApiKeys_mode_never_touches_the_provider()
    {
        var tenantId = Guid.NewGuid();
        var config = TenantPaymentConfig
            .Create(
                tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_123",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                DateTime.UtcNow
            )
            .Value;
        config.UpdateSecrets(
            EncryptedSecret.Create("cipher-key").Value,
            EncryptedSecret.Create("cipher-webhook").Value,
            Guid.Empty,
            DateTime.UtcNow
        );
        config.MarkActive(Guid.Empty, DateTime.UtcNow);

        var payments = new FakeTenantPaymentRepository();
        var configs = new FakeTenantPaymentConfigRepository(config);
        var connectAccounts = new ThrowingTenantConnectAccountRepository();
        var providerFactory = new ThrowingPaymentAdapterFactory();
        // Tampoco debería intentar descifrar ningún secreto para un cobro de $0.
        var secretProtector = new ThrowingSecretProtector();
        var audit = new FakePaymentAuditLogWriter();
        var metrics = new FakePaymentClientMetrics();
        var published = new List<object>();

        var command = new ChargeTenantPaymentCommand(
            tenantId,
            PaymentProviderCode.Stripe,
            AmountCents: 0,
            Currency: "USD",
            TaxpayerId: Guid.NewGuid(),
            PaymentPurposeKind.InvoicePayment,
            PurposeExternalReferenceId: "inv-100pct-off",
            PaymentMethodReference: "pm_unused",
            ReceiptEmail: null,
            IdempotencyKey: "charge-key-2",
            ActorUserId: Guid.Empty
        );

        var result = await ChargeTenantPaymentHandler.Handle(
            command,
            payments,
            configs,
            connectAccounts,
            providerFactory,
            secretProtector,
            Options.Create(
                new PlatformStripeCredentials { PlatformSecretKey = "sk_platform", ConnectWebhookSecret = "whsec" }
            ),
            audit,
            new FakeUnitOfWork(),
            InMemoryPaymentClient.RecordingBus(published),
            metrics,
            new FakeCorrelationContext(),
            NoOpLogger<TenantPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var payment = Assert.Single(payments.Added);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        // Monto cero también es "pagado": la factura no puede quedar abierta.
        var paid = Assert.Single(published.OfType<TenantPaymentSucceededIntegrationEvent>());
        Assert.Equal(payment.Id, paid.TenantPaymentId);
    }

    [Fact]
    public async Task DirectInvoiceCharge_Succeeded_PublishesTenantPaymentSucceeded()
    {
        // Cobro directo de una factura (sin link): antes no avisaba a Billing.
        var office = await DirectOffice.SeedAsync();
        var published = new List<object>();

        var result = await office.ChargeAsync("direct-invoice-1", published);

        Assert.True(result.IsSuccess);
        var paid = Assert.Single(published.OfType<TenantPaymentSucceededIntegrationEvent>());
        Assert.Equal(result.Value, paid.TenantPaymentId);
        Assert.Equal(nameof(PaymentPurposeKind.InvoicePayment), paid.PurposeKind);
        Assert.Equal("inv-direct", paid.ExternalReferenceId);
        Assert.Equal(2500, paid.AmountCents);
    }

    [Fact]
    public async Task DirectCharge_ReplayOfSameIdempotencyKey_DoesNotRepublish()
    {
        // Un reintento con la misma clave devuelve el pago existente sin volver a avisar.
        var office = await DirectOffice.SeedAsync();
        var published = new List<object>();

        await office.ChargeAsync("direct-invoice-2", published);
        await office.ChargeAsync("direct-invoice-2", published);

        Assert.Single(published.OfType<TenantPaymentSucceededIntegrationEvent>());
    }

    [Fact]
    public async Task DirectCharge_Declined_PublishesNothing()
    {
        var office = await DirectOffice.SeedAsync();
        office.Stripe.Status = PaymentStatus.Failed;
        var published = new List<object>();

        await office.ChargeAsync("direct-invoice-3", published);

        Assert.Empty(published);
    }

    /// <summary>Oficina Stripe directa en una base en memoria, con proveedor falso.</summary>
    private sealed class DirectOffice
    {
        private readonly PaymentClientDbContext _db = InMemoryPaymentClient.CreateDb();
        private readonly Guid _tenantId = Guid.NewGuid();

        public RecordingPaymentProvider Stripe { get; } =
            new(new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false));

        public static async Task<DirectOffice> SeedAsync()
        {
            var office = new DirectOffice();
            var config = TenantPaymentConfig
                .Create(
                    office._tenantId,
                    PaymentProviderCode.Stripe,
                    TenantPaymentMode.DirectApiKeys,
                    "pk_test_office",
                    StatementDescriptor.Create("ACME TAX SVC").Value,
                    DateTime.UtcNow
                )
                .Value;
            config.UpdateSecrets(
                EncryptedSecret.Create(new PrefixSecretProtector().Protect("sk_test_office")).Value,
                null,
                Guid.Empty,
                DateTime.UtcNow
            );
            config.MarkActive(Guid.Empty, DateTime.UtcNow);
            office._db.TenantPaymentConfigs.Add(config);
            await office._db.SaveChangesAsync();
            return office;
        }

        public Task<BuildingBlocks.Results.Result<Guid>> ChargeAsync(string idempotencyKey, List<object> published) =>
            ChargeTenantPaymentHandler.Handle(
                new ChargeTenantPaymentCommand(
                    _tenantId,
                    PaymentProviderCode.Stripe,
                    2500,
                    "USD",
                    TaxpayerId: null,
                    PaymentPurposeKind.InvoicePayment,
                    PurposeExternalReferenceId: "inv-direct",
                    PaymentMethodReference: "pm_card_visa",
                    ReceiptEmail: null,
                    IdempotencyKey: idempotencyKey,
                    ActorUserId: Guid.NewGuid()
                ),
                new TenantPaymentRepository(_db),
                new TenantPaymentConfigRepository(_db),
                new TenantConnectAccountRepository(_db),
                InMemoryPaymentClient.FakeAdapters(Stripe),
                new PrefixSecretProtector(),
                Options.Create(
                    new PlatformStripeCredentials { PlatformSecretKey = "unused", ConnectWebhookSecret = "unused" }
                ),
                new PaymentAuditLogWriter(_db),
                _db,
                InMemoryPaymentClient.RecordingBus(published),
                new NoOpPaymentClientMetrics(),
                new FixedCorrelationContext(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantPayment>.Instance,
                CancellationToken.None
            );
    }
}

file sealed class FakeTenantPaymentRepository : ITenantPaymentRepository
{
    public List<TenantPayment> Added { get; } = [];

    public Task<TenantPayment?> GetByIdAsync(Guid tenantPaymentId, Guid tenantId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<TenantPayment?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default
    ) => Task.FromResult<TenantPayment?>(null);

    public Task<TenantPayment?> GetByExternalReferenceAsync(
        Guid tenantId,
        PaymentProviderCode code,
        string providerChargeReference,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<AwaitingProviderPayment>> GetAwaitingProviderConfirmationAsync(
        DateTime stuckBeforeUtc,
        DateTime createdAfterUtc,
        int batchSize,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TenantPayment>> GetDueForRetryAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TenantPayment>> SearchAdminAsync(
        Guid? tenantId,
        PaymentStatus? status,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<bool> AnyForProviderAsync(Guid tenantId, PaymentProviderCode code, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlySet<PaymentProviderCode>> GetConfiguredProvidersWithPaymentsAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<bool> AnyInProgressForReferenceAsync(
        Guid tenantId,
        string externalReferenceId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<bool> AnySucceededForReferenceSinceAsync(
        Guid tenantId,
        string externalReferenceId,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TenantPayment>> GetByIdsAcrossTenantsAsync(
        IReadOnlyCollection<Guid> tenantPaymentIds,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task AddAsync(TenantPayment payment, CancellationToken ct = default)
    {
        Added.Add(payment);
        return Task.CompletedTask;
    }
}

file sealed class FakeTenantPaymentConfigRepository(TenantPaymentConfig config) : ITenantPaymentConfigRepository
{
    public Task<TenantPaymentConfig?> GetByTenantAndProviderAsync(
        Guid tenantId,
        PaymentProviderCode code,
        CancellationToken ct = default
    ) => Task.FromResult<TenantPaymentConfig?>(config);

    public Task<TenantPaymentConfig?> GetByIdAsync(
        Guid tenantPaymentConfigId,
        Guid tenantId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TenantPaymentConfig>> GetActiveByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TenantPaymentConfig>> GetAllByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task AddAsync(TenantPaymentConfig config, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<TenantPaymentConfig>> GetStalePendingWebhookSetupsAsync(
        DateTime requestedBeforeUtc,
        int batchSize,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public void Remove(TenantPaymentConfig config) => throw new NotImplementedException();
}

file sealed class ThrowingTenantConnectAccountRepository : ITenantConnectAccountRepository
{
    public Task<TenantConnectAccount?> GetByTenantAndProviderAsync(
        Guid tenantId,
        PaymentProviderCode code,
        CancellationToken ct = default
    ) => throw new InvalidOperationException("A zero-amount charge must never look up a Connect account.");

    public Task<TenantConnectAccount?> GetByStripeConnectAccountIdAsync(
        string stripeConnectAccountId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task AddAsync(TenantConnectAccount account, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

file sealed class ThrowingPaymentAdapterFactory : IPaymentAdapterFactory
{
    public IPaymentProvider Resolve(PaymentProviderCode code) =>
        throw new InvalidOperationException("A zero-amount charge must never resolve a payment provider adapter.");

    public bool IsRegistered(PaymentProviderCode code) =>
        throw new InvalidOperationException("A zero-amount charge must never check a payment provider adapter.");

    public IReadOnlyList<PaymentProviderDescriptor> GetAvailableProviders() => throw new NotSupportedException();

    public PaymentProviderDescriptor? FindDescriptor(PaymentProviderCode code) => throw new NotSupportedException();
}

file sealed class ThrowingSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => throw new InvalidOperationException("Unexpected secret access.");

    public bool TryUnprotect(string? protectedValue, out string plaintext, out SecretUnprotectFailure failure) =>
        throw new InvalidOperationException("A zero-amount charge must never decrypt a provider secret.");
}

file sealed class FakePaymentAuditLogWriter : IPaymentAuditLogWriter
{
    public List<PaymentAuditEntry> Entries { get; } = [];

    public Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

file sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
}

file sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "test-correlation-id";

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new NoOpDisposable();

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

file sealed class FakePaymentClientMetrics : IPaymentClientMetrics
{
    public List<(long AmountCents, string Currency)> PaymentsSucceeded { get; } = [];
    public List<(long FeeCents, string Currency)> PlatformFees { get; } = [];

    public void RecordPaymentSucceeded(long amountCents, string currency) =>
        PaymentsSucceeded.Add((amountCents, currency));

    public void RecordPlatformFee(long feeCents, string currency) => PlatformFees.Add((feeCents, currency));

    public void RecordConnectOnboardingCompleted() { }

    public void RecordPaymentLinkCreated() { }

    public void RecordPaymentLinkUsed() { }

    public void RecordRefund(string provider) { }

    public void RecordWebhookReceived(string provider) { }

    public void RecordWebhookDuplicate(string provider) { }

    public void RecordWebhookSignatureFailed(string provider) { }

    public void RecordWebhookNeedsReview(string provider, string reason) { }
}

file sealed class NoOpLogger<T> : ILogger<T>
{
    public static readonly NoOpLogger<T> Instance = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) { }
}
