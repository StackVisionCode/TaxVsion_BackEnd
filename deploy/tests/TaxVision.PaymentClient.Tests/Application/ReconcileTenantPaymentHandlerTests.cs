using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ReconcileTenantPayment;
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
        var paid = Assert.Single(_published.OfType<TenantPaymentSucceededIntegrationEvent>());
        Assert.Equal(payment.Id, paid.TenantPaymentId);
        Assert.Single(_published.OfType<PaymentLinkUsedIntegrationEvent>());
        var (credentials, lookup) = Assert.Single(_stripe.StatusLookups);
        Assert.Equal("sk_test_office", credentials.SecretKey);
        Assert.Equal("pi_stuck", lookup.ProviderChargeReference);
        Assert.Null(lookup.OnBehalfOf);
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
        Assert.Empty(_published);
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
        Assert.Empty(_published);
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
        Assert.Empty(_published);
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

    private Task<Result<TenantPaymentReconciliation>> ReconcileAsync(Guid paymentId) =>
        ReconcileTenantPaymentHandler.Handle(
            new ReconcileTenantPaymentCommand(_tenantId, paymentId),
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
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<TenantPayment>.Instance,
            CancellationToken.None
        );

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
        link.AttachPaymentAttempt(payment.Id, LongAgo);
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
