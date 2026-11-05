using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;
using TaxVision.PaymentClient.Application.PaymentLinks.Queries;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Una oficina con Stripe y PayPal activos a la vez: el checkout ofrece los dos y cada cobro
/// sale exactamente por el proveedor que eligió el cliente, con las credenciales de ese proveedor.</summary>
public sealed class MultipleActiveProvidersTests
{
    private const string PayPalSandbox = "https://api-m.sandbox.paypal.com";
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    private readonly PaymentClientDbContext _db = InMemoryPaymentClient.CreateDb();
    private readonly RecordingPaymentProvider _stripe = new(
        new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false)
    );
    private readonly RecordingPaymentProvider _payPal = new(
        new(PaymentProviderCode.PayPal, "PayPal", "Client ID", "Client secret", false, true)
    );
    private readonly IPaymentAdapterFactory _adapters;
    private readonly List<object> _published = [];
    private readonly Guid _tenantId = Guid.NewGuid();

    public MultipleActiveProvidersTests() => _adapters = InMemoryPaymentClient.FakeAdapters(_stripe, _payPal);

    [Fact]
    public async Task The_checkout_offers_every_active_provider_with_its_own_public_key()
    {
        var link = await SeedOfficeWithStripeAndPayPalAsync();

        var result = await GetPaymentLinkByTokenHandler.Handle(
            new GetPaymentLinkByTokenQuery(link.Token.Value),
            new PaymentLinkRepository(_db),
            new PayableReferenceRepository(_db),
            new TenantPaymentConfigRepository(_db),
            _adapters,
            new TenantRegistry(_db),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value.Methods,
            stripe =>
            {
                Assert.Equal("Stripe", stripe.ProviderCode);
                Assert.Equal("pk_test_office", stripe.PublishableKey);
                Assert.Null(stripe.ApiBaseUrl);
            },
            payPal =>
            {
                Assert.Equal("PayPal", payPal.ProviderCode);
                Assert.Equal("PayPal", payPal.DisplayName);
                Assert.Equal("paypal-client-id", payPal.PublishableKey);
                Assert.Equal(PayPalSandbox, payPal.ApiBaseUrl);
            }
        );
    }

    [Fact]
    public async Task Paying_the_link_with_PayPal_charges_only_PayPal_with_the_PayPal_credentials()
    {
        var link = await SeedOfficeWithStripeAndPayPalAsync();

        var response = await RedeemAsync(link, PaymentProviderCode.PayPal);

        Assert.Equal(nameof(PaymentStatus.Succeeded), response.Status);
        Assert.Empty(_stripe.Charges);
        var credentials = Assert.Single(_payPal.Charges);
        Assert.Equal("paypal-client-secret", credentials.SecretKey);
        Assert.Equal("paypal-client-id", credentials.PublishableKey);
        Assert.Equal(PayPalSandbox, credentials.ApiBaseUrl);

        var payment = await _db.TenantPayments.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(PaymentProviderCode.PayPal, payment.ProviderCode);
        Assert.Equal("PayPal", Assert.Single(_published.OfType<TenantPaymentSucceededIntegrationEvent>()).ProviderCode);
        Assert.Equal(PaymentLinkStatus.Used, (await ReloadAsync(link)).Status);
    }

    [Fact]
    public async Task Paying_the_link_with_Stripe_charges_only_Stripe_with_the_Stripe_credentials()
    {
        var link = await SeedOfficeWithStripeAndPayPalAsync();

        var response = await RedeemAsync(link, PaymentProviderCode.Stripe);

        Assert.Equal(nameof(PaymentStatus.Succeeded), response.Status);
        Assert.Empty(_payPal.Charges);
        Assert.Equal("sk_test_office", Assert.Single(_stripe.Charges).SecretKey);
        Assert.Equal(
            PaymentProviderCode.Stripe,
            (await _db.TenantPayments.IgnoreQueryFilters().SingleAsync()).ProviderCode
        );
    }

    [Fact]
    public async Task Paying_with_a_provider_the_office_turned_off_charges_nobody()
    {
        var link = await SeedOfficeWithStripeAndPayPalAsync();
        var payPal = await new TenantPaymentConfigRepository(_db).GetByTenantAndProviderAsync(
            _tenantId,
            PaymentProviderCode.PayPal
        );
        payPal!.Deactivate("paused", Guid.Empty, NowUtc);
        await _db.SaveChangesAsync();

        var result = await RedeemHandlerAsync(link, PaymentProviderCode.PayPal);

        Assert.True(result.IsFailure);
        Assert.Empty(_payPal.Charges);
        Assert.Empty(_stripe.Charges);
        Assert.Empty(await _db.TenantPayments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task A_staff_charge_with_PayPal_goes_through_PayPal_while_Stripe_is_also_active()
    {
        await SeedOfficeWithStripeAndPayPalAsync();

        var result = await ChargeTenantPaymentHandler.Handle(
            new ChargeTenantPaymentCommand(
                _tenantId,
                PaymentProviderCode.PayPal,
                2500,
                "USD",
                TaxpayerId: null,
                PaymentPurposeKind.Other,
                PurposeExternalReferenceId: null,
                PaymentMethodReference: "paypal-order-1",
                ReceiptEmail: null,
                IdempotencyKey: "staff-charge-1",
                ActorUserId: Guid.NewGuid()
            ),
            new TenantPaymentRepository(_db),
            new TenantPaymentConfigRepository(_db),
            new TenantConnectAccountRepository(_db),
            _adapters,
            new PrefixSecretProtector(),
            Options.Create(
                new PlatformStripeCredentials { PlatformSecretKey = "unused", ConnectWebhookSecret = "unused" }
            ),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus(_published),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<TenantPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(_stripe.Charges);
        Assert.Equal("paypal-client-secret", Assert.Single(_payPal.Charges).SecretKey);
    }

    private async Task<RedeemPaymentLinkResponse> RedeemAsync(PaymentLink link, PaymentProviderCode provider)
    {
        var result = await RedeemHandlerAsync(link, provider);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private Task<BuildingBlocks.Results.Result<RedeemPaymentLinkResponse>> RedeemHandlerAsync(
        PaymentLink link,
        PaymentProviderCode provider
    ) =>
        RedeemPaymentLinkHandler.Handle(
            new RedeemPaymentLinkCommand(link.Token.Value, provider, "client-payment-token", ReceiptEmail: null),
            new PaymentLinkRepository(_db),
            new TenantPaymentRepository(_db),
            new TenantPaymentConfigRepository(_db),
            _adapters,
            new PrefixSecretProtector(),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus(_published),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<PaymentLink>.Instance,
            CancellationToken.None
        );

    private async Task<PaymentLink> ReloadAsync(PaymentLink link) =>
        (await new PaymentLinkRepository(_db).GetByTokenAsync(link.Token.Value))!;

    private async Task<PaymentLink> SeedOfficeWithStripeAndPayPalAsync()
    {
        _db.Tenants.Add(
            Tenant.Register(_tenantId, "Acme Tax Office", "acme-tax", TenantKind.Customer, "Etc/UTC", NowUtc).Value
        );

        var stripe = NewConfig(PaymentProviderCode.Stripe, "pk_test_office", apiBaseUrl: null);
        stripe.UpdateSecrets(Encrypted("sk_test_office"), Encrypted("whsec_office"), Guid.Empty, NowUtc);
        stripe.MarkActive(Guid.Empty, NowUtc);

        var payPal = NewConfig(PaymentProviderCode.PayPal, "paypal-client-id", PayPalSandbox);
        payPal.UpdateSecrets(Encrypted("paypal-client-secret"), null, Guid.Empty, NowUtc);
        payPal.MarkActive(Guid.Empty, NowUtc);

        _db.TenantPaymentConfigs.AddRange(stripe, payPal);

        var link = PaymentLink
            .Create(
                _tenantId,
                null,
                Money.Create(2500, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.Other, null).Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(7),
                Guid.Empty,
                NowUtc
            )
            .Value;
        _db.PaymentLinks.Add(link);

        await _db.SaveChangesAsync();
        return link;
    }

    private TenantPaymentConfig NewConfig(PaymentProviderCode provider, string publicKey, string? apiBaseUrl) =>
        TenantPaymentConfig
            .Create(
                _tenantId,
                provider,
                TenantPaymentMode.DirectApiKeys,
                publicKey,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                NowUtc,
                apiBaseUrl
            )
            .Value;

    private static EncryptedSecret Encrypted(string plaintext) =>
        EncryptedSecret.Create(new PrefixSecretProtector().Protect(plaintext)).Value;
}
