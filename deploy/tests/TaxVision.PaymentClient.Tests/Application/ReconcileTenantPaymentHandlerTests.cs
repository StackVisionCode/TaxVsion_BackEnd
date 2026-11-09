using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ReconcileTenantPayment;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>La revisión de atascados pregunta al proveedor y aplica su estado por el mismo camino del
/// webhook; nunca inventa un fallo y anota cada consulta para rotar la cola.</summary>
public sealed class ReconcileTenantPaymentHandlerTests
{
    private const long AmountCents = 2500;
    private static readonly DateTime LongAgo = DateTime.UtcNow.AddHours(-2);

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly PaymentClientDbContext _db;
    private readonly RecordingPaymentProvider _stripe = new(
        new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false)
    );
    private readonly List<object> _published = [];
    private readonly Guid _tenantId = Guid.NewGuid();

    public ReconcileTenantPaymentHandlerTests() => _db = InMemoryPaymentClient.CreateDb(_databaseName);

    [Fact]
    public async Task A_charge_the_provider_confirmed_is_marked_paid_and_completes_its_link()
    {
        var config = await SeedOfficeAsync();
        var (payment, link) = await SeedStuckLinkPaymentAsync(config);
        _stripe.ChargeStatus = lookup => Ok(Paid(lookup.ProviderChargeReference, AmountCents, "usd"));

        var result = await ReconcileAsync(payment.Id);

        Assert.Equal(TenantPaymentReconciliation.Resolved, result.Value);
        var saved = await ReloadAsync(payment.Id);
        Assert.Equal(PaymentStatus.Succeeded, saved.Status);
        Assert.NotNull(saved.LastProviderCheckAtUtc);
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        Assert.Equal(
            PaymentLinkStatus.Used,
            (await fresh.PaymentLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link.Id)).Status
        );
        var paid = Assert.Single((await EmittedAsync()).OfType<TenantPaymentSucceededIntegrationEvent>());
        Assert.Equal(payment.Id, paid.TenantPaymentId);
        Assert.Single(_published.OfType<PaymentLinkUsedIntegrationEvent>());
        var (credentials, lookup) = Assert.Single(_stripe.StatusLookups);
        Assert.Equal("sk_test_office", credentials.SecretKey);
        Assert.Equal("pi_stuck", lookup.ProviderChargeReference);
        Assert.Null(lookup.OnBehalfOf);
    }

    [Fact]
    public async Task Reconcile_ProviderSucceeded_WithExpiredLink_PublishesSignal()
    {
        // El link venció mientras el cobro esperaba: el pago igual avisa a la factura.
        var config = await SeedOfficeAsync();
        var (payment, link) = await SeedStuckLinkPaymentAsync(config);
        link.Expire(DateTime.UtcNow);
        await _db.SaveChangesAsync();
        _stripe.ChargeStatus = lookup => Ok(Paid(lookup.ProviderChargeReference, AmountCents, "usd"));

        var result = await ReconcileAsync(payment.Id);

        Assert.Equal(TenantPaymentReconciliation.Resolved, result.Value);
        var paid = Assert.Single((await EmittedAsync()).OfType<TenantPaymentSucceededIntegrationEvent>());
        Assert.Equal(payment.Id, paid.TenantPaymentId);
        Assert.Equal("inv-1", paid.ExternalReferenceId);
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        Assert.Equal(
            PaymentLinkStatus.Used,
            (await fresh.PaymentLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link.Id)).Status
        );
    }

    [Fact]
    public async Task Reconcile_ProviderSucceeded_WithoutLink_PublishesSignal()
    {
        // Cobro directo sin link: la señal no depende de que exista uno.
        var config = await SeedOfficeAsync();
        var payment = await SeedStuckPaymentAsync(config);
        _stripe.ChargeStatus = lookup => Ok(Paid(lookup.ProviderChargeReference, AmountCents, "usd"));

        await ReconcileAsync(payment.Id);

        var paid = Assert.Single((await EmittedAsync()).OfType<TenantPaymentSucceededIntegrationEvent>());
        Assert.Equal(payment.Id, paid.TenantPaymentId);
        Assert.Empty(_published.OfType<PaymentLinkUsedIntegrationEvent>());
    }

    [Theory]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.RequiresAction)]
    public async Task A_charge_the_provider_has_not_resolved_stays_as_it_is_but_the_check_is_recorded(
        PaymentStatus reported
    )
    {
        var config = await SeedOfficeAsync();
        var payment = await SeedStuckPaymentAsync(config);
        _stripe.ChargeStatus = lookup =>
            Ok(new WebhookEventPayload(lookup.ProviderChargeReference, reported, null, null, null));

        var result = await ReconcileAsync(payment.Id);

        Assert.Equal(TenantPaymentReconciliation.StillPending, result.Value);
        var saved = await ReloadAsync(payment.Id);
        Assert.Equal(PaymentStatus.Processing, saved.Status);
        Assert.NotNull(saved.LastProviderCheckAtUtc);
        Assert.Equal(payment.UpdatedAtUtc, saved.UpdatedAtUtc);
        Assert.Empty(await EmittedAsync());
    }

    [Fact]
    public async Task A_decline_reported_by_the_provider_fails_the_payment_with_its_code()
    {
        var config = await SeedOfficeAsync();
        var payment = await SeedStuckPaymentAsync(config);
        _stripe.ChargeStatus = lookup =>
            Ok(
                new WebhookEventPayload(
                    lookup.ProviderChargeReference,
                    PaymentStatus.Failed,
                    "card_declined",
                    "Declined.",
                    null
                )
            );

        await ReconcileAsync(payment.Id);

        var saved = await ReloadAsync(payment.Id);
        Assert.Equal(PaymentStatus.Failed, saved.Status);
        Assert.Equal("card_declined", saved.FailureCode);
        Assert.Empty(await EmittedAsync());
    }

    [Fact]
    public async Task A_paid_amount_that_does_not_match_is_not_applied()
    {
        var config = await SeedOfficeAsync();
        var payment = await SeedStuckPaymentAsync(config);
        _stripe.ChargeStatus = lookup => Ok(Paid(lookup.ProviderChargeReference, 100, "usd"));

        var result = await ReconcileAsync(payment.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("WebhookEvent.AmountMismatch", result.Error.Code);
        var saved = await ReloadAsync(payment.Id);
        Assert.Equal(PaymentStatus.Processing, saved.Status);
        Assert.NotNull(saved.LastProviderCheckAtUtc);
        Assert.Empty(await EmittedAsync());
    }

    [Fact]
    public async Task Reconcile_ProviderSucceededWithDifferentAmount_RecordsNeedsReview()
    {
        var config = await SeedOfficeAsync();
        var payment = await SeedStuckPaymentAsync(config);
        _stripe.ChargeStatus = lookup => Ok(Paid(lookup.ProviderChargeReference, 100, "usd"));
        var metrics = new NeedsReviewMetrics();

        await ReconcileAsync(payment.Id, metrics: metrics);

        Assert.Equal([("Stripe", "WebhookEvent.AmountMismatch")], metrics.NeedsReview);
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        Assert.True(
            await fresh.AuditEntries.AnyAsync(entry =>
                entry.AggregateId == payment.Id
                && entry.Action == PaymentAuditAction.TenantPaymentProviderSuccessNeedsReview
            )
        );
        Assert.Equal(PaymentStatus.Processing, (await ReloadAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task A_provider_error_is_returned_and_retried_later_without_touching_the_payment()
    {
        var config = await SeedOfficeAsync();
        var payment = await SeedStuckPaymentAsync(config);
        _stripe.ChargeStatus = _ =>
            Result.Failure<WebhookEventPayload>(new Error("Stripe.ChargeStatus.Failed", "boom"));

        var result = await ReconcileAsync(payment.Id);

        Assert.Equal("Stripe.ChargeStatus.Failed", result.Error.Code);
        var saved = await ReloadAsync(payment.Id);
        Assert.Equal(PaymentStatus.Processing, saved.Status);
        Assert.NotNull(saved.LastProviderCheckAtUtc);
    }

    [Fact]
    public async Task An_inactive_config_skips_the_provider_and_is_retried_later()
    {
        var config = await SeedOfficeAsync();
        var payment = await SeedStuckPaymentAsync(config);
        config.Deactivate("paused", Guid.Empty, DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var result = await ReconcileAsync(payment.Id);

        Assert.Equal("TenantPaymentConfig.NotActive", result.Error.Code);
        Assert.Empty(_stripe.StatusLookups);
        Assert.Equal(PaymentStatus.Processing, (await ReloadAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task A_payment_already_resolved_is_left_alone()
    {
        var config = await SeedOfficeAsync();
        var payment = await SeedStuckPaymentAsync(config);
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);
        await _db.SaveChangesAsync();

        var result = await ReconcileAsync(payment.Id);

        Assert.Equal(TenantPaymentReconciliation.AlreadyResolved, result.Value);
        Assert.Empty(_stripe.StatusLookups);
    }

    [Fact]
    public async Task A_Connect_charge_is_looked_up_in_the_connected_account_with_the_platform_key()
    {
        var config = TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.Connect,
                "pk_test_platform",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                LongAgo
            )
            .Value;
        config.MarkActiveViaConnect(Guid.Empty, LongAgo);
        _db.TenantPaymentConfigs.Add(config);
        _db.TenantConnectAccounts.Add(
            TenantConnectAccount
                .Create(
                    _tenantId,
                    PaymentProviderCode.Stripe,
                    ConnectAccountType.Express,
                    StripeConnectAccountId.Create("acct_office").Value,
                    LongAgo
                )
                .Value
        );
        var payment = NewPayment();
        payment.MarkProcessingViaConnect(
            "pi_connect",
            SplitPaymentBreakdown.Create(AmountCents - 100, 100, null).Value,
            null,
            null,
            Guid.Empty,
            LongAgo
        );
        _db.TenantPayments.Add(payment);
        await _db.SaveChangesAsync();
        _stripe.ChargeStatus = lookup => Ok(Paid(lookup.ProviderChargeReference, AmountCents, "usd"));

        var result = await ReconcileAsync(payment.Id);

        Assert.Equal(TenantPaymentReconciliation.Resolved, result.Value);
        var (credentials, lookup) = Assert.Single(_stripe.StatusLookups);
        Assert.Equal("sk_platform", credentials.SecretKey);
        Assert.Equal("pi_connect", lookup.ProviderChargeReference);
        Assert.Equal("acct_office", lookup.OnBehalfOf);
    }

    [Fact]
    public async Task AbandonedRequiresAction_AfterTtl_CancelsAtProviderBeforeClosing()
    {
        var config = await SeedOfficeAsync();
        var (payment, link) = await SeedRequiresActionLinkPaymentAsync(config, LongAgo);
        _stripe.ChargeStatus = lookup =>
            Ok(new WebhookEventPayload(lookup.ProviderChargeReference, PaymentStatus.RequiresAction, null, null, null));
        _stripe.CancelCharge = lookup =>
            Ok(
                new WebhookEventPayload(
                    lookup.ProviderChargeReference,
                    PaymentStatus.Cancelled,
                    "abandoned",
                    null,
                    null
                )
            );

        var result = await ReconcileAsync(payment.Id, Ttl);

        Assert.Equal(TenantPaymentReconciliation.Resolved, result.Value);
        var (credentials, lookup) = Assert.Single(_stripe.Cancellations);
        Assert.Equal("sk_test_office", credentials.SecretKey);
        Assert.Equal("pi_stuck", lookup.ProviderChargeReference);
        var saved = await ReloadAsync(payment.Id);
        Assert.Equal(PaymentStatus.Cancelled, saved.Status);
        Assert.Equal("abandoned-3ds-ttl", saved.FailureReason);
        Assert.Empty(await EmittedAsync());

        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        var audit = Assert.Single(
            await fresh
                .AuditEntries.IgnoreQueryFilters()
                .Where(a => a.AggregateId == payment.Id && a.Action == PaymentAuditAction.TenantPaymentCancelled)
                .ToListAsync()
        );
        Assert.Equal("System", audit.ActorType);
        Assert.Equal(Guid.Empty, audit.ActorUserId);
        Assert.Equal("abandoned-3ds-ttl", audit.Reason);

        var savedLink = await fresh.PaymentLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link.Id);
        Assert.Equal(PaymentLinkStatus.Active, savedLink.Status);
        Assert.True(savedLink.AttachPaymentAttempt(Guid.NewGuid(), saved.Status, DateTime.UtcNow).IsSuccess);
    }

    [Fact]
    public async Task AbandonedRequiresAction_WhenProviderCancelFails_StaysOpenForNextRound()
    {
        var config = await SeedOfficeAsync();
        var (payment, _) = await SeedRequiresActionLinkPaymentAsync(config, LongAgo);
        _stripe.ChargeStatus = lookup =>
            Ok(new WebhookEventPayload(lookup.ProviderChargeReference, PaymentStatus.RequiresAction, null, null, null));
        _stripe.CancelCharge = _ =>
            Result.Failure<WebhookEventPayload>(new Error("Stripe.CancelCharge.Failed", "boom"));

        var result = await ReconcileAsync(payment.Id, Ttl);

        Assert.Equal("Stripe.CancelCharge.Failed", result.Error.Code);
        var saved = await ReloadAsync(payment.Id);
        Assert.Equal(PaymentStatus.RequiresAction, saved.Status);
        Assert.NotNull(saved.LastProviderCheckAtUtc);
    }

    [Fact]
    public async Task AbandonedButProviderSaysSucceeded_IsMarkedPaidNotCancelled()
    {
        var config = await SeedOfficeAsync();
        var (payment, link) = await SeedRequiresActionLinkPaymentAsync(config, LongAgo);
        _stripe.ChargeStatus = lookup => Ok(Paid(lookup.ProviderChargeReference, AmountCents, "usd"));

        var result = await ReconcileAsync(payment.Id, Ttl);

        Assert.Equal(TenantPaymentReconciliation.Resolved, result.Value);
        Assert.Empty(_stripe.Cancellations);
        Assert.Equal(PaymentStatus.Succeeded, (await ReloadAsync(payment.Id)).Status);
        Assert.Single((await EmittedAsync()).OfType<TenantPaymentSucceededIntegrationEvent>());
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        Assert.Equal(
            PaymentLinkStatus.Used,
            (await fresh.PaymentLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link.Id)).Status
        );
    }

    [Fact]
    public async Task AbandonedCancelRace_ProviderCompletedMeanwhile_IsMarkedPaidNotCancelled()
    {
        var config = await SeedOfficeAsync();
        var (payment, link) = await SeedRequiresActionLinkPaymentAsync(config, LongAgo);
        _stripe.ChargeStatus = lookup =>
            Ok(new WebhookEventPayload(lookup.ProviderChargeReference, PaymentStatus.RequiresAction, null, null, null));
        // El adaptador vio que el cobro se completó al intentar cancelarlo.
        _stripe.CancelCharge = lookup => Ok(Paid(lookup.ProviderChargeReference, AmountCents, "usd"));

        var result = await ReconcileAsync(payment.Id, Ttl);

        Assert.Equal(TenantPaymentReconciliation.Resolved, result.Value);
        Assert.Single(_stripe.Cancellations);
        Assert.Equal(PaymentStatus.Succeeded, (await ReloadAsync(payment.Id)).Status);
        Assert.Single((await EmittedAsync()).OfType<TenantPaymentSucceededIntegrationEvent>());
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        Assert.Equal(
            PaymentLinkStatus.Used,
            (await fresh.PaymentLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link.Id)).Status
        );
    }

    [Fact]
    public async Task RequiresAction_BeforeTtl_IsNotCancelled()
    {
        var config = await SeedOfficeAsync();
        var (payment, _) = await SeedRequiresActionLinkPaymentAsync(config, DateTime.UtcNow.AddMinutes(-5));
        _stripe.ChargeStatus = lookup =>
            Ok(new WebhookEventPayload(lookup.ProviderChargeReference, PaymentStatus.RequiresAction, null, null, null));

        var result = await ReconcileAsync(payment.Id, Ttl);

        Assert.Equal(TenantPaymentReconciliation.StillPending, result.Value);
        Assert.Empty(_stripe.Cancellations);
        Assert.Equal(PaymentStatus.RequiresAction, (await ReloadAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task Processing_AfterTtl_IsNotCancelled()
    {
        var config = await SeedOfficeAsync();
        var payment = await SeedStuckPaymentAsync(config);
        _stripe.ChargeStatus = lookup =>
            Ok(new WebhookEventPayload(lookup.ProviderChargeReference, PaymentStatus.Processing, null, null, null));

        var result = await ReconcileAsync(payment.Id, Ttl);

        Assert.Equal(TenantPaymentReconciliation.StillPending, result.Value);
        Assert.Empty(_stripe.Cancellations);
        Assert.Equal(PaymentStatus.Processing, (await ReloadAsync(payment.Id)).Status);
    }

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private async Task<(TenantPayment Payment, PaymentLink Link)> SeedRequiresActionLinkPaymentAsync(
        TenantPaymentConfig config,
        DateTime atUtc
    )
    {
        var (payment, link) = await SeedStuckLinkPaymentAsync(config);
        payment.MarkRequiresAction("redirect_to_url", "https://3ds.test", Guid.Empty, atUtc);
        await _db.SaveChangesAsync();
        return (payment, link);
    }

    private Task<Result<TenantPaymentReconciliation>> ReconcileAsync(
        Guid paymentId,
        TimeSpan? abandonAfter = null,
        IPaymentClientMetrics? metrics = null
    ) =>
        ReconcileTenantPaymentHandler.Handle(
            new ReconcileTenantPaymentCommand(_tenantId, paymentId, abandonAfter),
            new TenantPaymentRepository(_db),
            new TenantPaymentConfigRepository(_db),
            new TenantConnectAccountRepository(_db),
            InMemoryPaymentClient.FakeAdapters(_stripe),
            new PrefixSecretProtector(),
            Options.Create(
                new PlatformStripeCredentials { PlatformSecretKey = "sk_platform", ConnectWebhookSecret = "unused" }
            ),
            new PaymentLinkRepository(_db),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus(_published),
            metrics ?? new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<TenantPayment>.Instance,
            CancellationToken.None
        );

    // Lo publicado más la señal "pagado" que devuelve el handler del evento de dominio del pago.
    private Task<IReadOnlyList<object>> EmittedAsync() => DomainEventReplay.EmittedAsync(_published, _db);

    private static Result<WebhookEventPayload> Ok(WebhookEventPayload payload) => Result.Success(payload);

    private static WebhookEventPayload Paid(string reference, long cents, string currency) =>
        new(reference, PaymentStatus.Succeeded, null, null, null, PaidAmountCents: cents, PaidCurrency: currency);

    private async Task<TenantPayment> ReloadAsync(Guid paymentId)
    {
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        return await fresh.TenantPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == paymentId);
    }

    private TenantPayment NewPayment() =>
        TenantPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"stuck-{Guid.NewGuid():N}").Value,
                Money.Create(AmountCents, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-1").Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                LongAgo
            )
            .Value;

    private async Task<TenantPayment> SeedStuckPaymentAsync(TenantPaymentConfig config)
    {
        var payment = NewPayment();
        payment.MarkProcessing(
            ExternalPaymentReference.Create(config.ProviderCode, "pi_stuck").Value,
            null,
            null,
            Guid.Empty,
            LongAgo
        );
        _db.TenantPayments.Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    private async Task<(TenantPayment Payment, PaymentLink Link)> SeedStuckLinkPaymentAsync(TenantPaymentConfig config)
    {
        var link = PaymentLink
            .Create(
                _tenantId,
                null,
                Money.Create(AmountCents, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-1").Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(7),
                Guid.Empty,
                LongAgo
            )
            .Value;
        var payment = await SeedStuckPaymentAsync(config);
        link.AttachPaymentAttempt(payment.Id, null, LongAgo);
        _db.PaymentLinks.Add(link);
        await _db.SaveChangesAsync();
        return (payment, link);
    }

    private async Task<TenantPaymentConfig> SeedOfficeAsync()
    {
        _db.Tenants.Add(
            Tenant.Register(_tenantId, "Acme Tax Office", "acme-tax", TenantKind.Customer, "Etc/UTC", LongAgo).Value
        );
        var config = TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_office",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                LongAgo
            )
            .Value;
        // Sin webhook secret: justo el caso que la revisión cubre.
        config.UpdateSecrets(
            EncryptedSecret.Create(new PrefixSecretProtector().Protect("sk_test_office")).Value,
            null,
            Guid.Empty,
            LongAgo
        );
        config.MarkActive(Guid.Empty, LongAgo);
        _db.TenantPaymentConfigs.Add(config);
        await _db.SaveChangesAsync();
        return config;
    }
}

file sealed class NeedsReviewMetrics : IPaymentClientMetrics
{
    public List<(string Provider, string Reason)> NeedsReview { get; } = [];

    public void RecordWebhookNeedsReview(string provider, string reason) => NeedsReview.Add((provider, reason));

    public void RecordPaymentSucceeded(long amountCents, string currency) { }

    public void RecordPlatformFee(long feeCents, string currency) { }

    public void RecordConnectOnboardingCompleted() { }

    public void RecordPaymentLinkCreated() { }

    public void RecordPaymentLinkUsed() { }

    public void RecordRefund(string provider) { }

    public void RecordWebhookReceived(string provider) { }

    public void RecordWebhookDuplicate(string provider) { }

    public void RecordWebhookSignatureFailed(string provider) { }
}
