using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Queries;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>El pago guarda quién pagó: el email del recibo (del comando) y lo que devuelve el proveedor,
/// y el detalle del pago lo expone.</summary>
public sealed class PayerDetailsPersistenceTests
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly PaymentClientDbContext _db;
    private readonly RecordingPaymentProvider _stripe = new(
        new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false)
    );
    private readonly Guid _tenantId = Guid.NewGuid();

    public PayerDetailsPersistenceTests() => _db = InMemoryPaymentClient.CreateDb(_databaseName);

    [Fact]
    public async Task Redeeming_a_link_saves_the_receipt_email_and_the_provider_payer()
    {
        var link = await SeedOfficeAsync();
        _stripe.Payer = new ProviderPayerDetails("Jane Doe", "jane@example.com", "visa", "4242");

        var result = await RedeemAsync(link, receiptEmail: " client@example.com ");

        Assert.True(result.IsSuccess);
        Assert.Equal("client@example.com", Assert.Single(_stripe.Requests).ReceiptEmail?.Trim());
        var saved = await ReloadPaymentAsync(result.Value.TenantPaymentId);
        Assert.Equal("client@example.com", saved.Payer.ReceiptEmail);
        Assert.Equal("Jane Doe", saved.Payer.PayerName);
        Assert.Equal("jane@example.com", saved.Payer.PayerEmail);
        Assert.Equal("visa", saved.Payer.CardBrand);
        Assert.Equal("4242", saved.Payer.CardLast4);
    }

    [Fact]
    public async Task A_declined_charge_still_keeps_the_card_that_was_tried()
    {
        var link = await SeedOfficeAsync();
        _stripe.Status = PaymentStatus.Failed;
        _stripe.Payer = new ProviderPayerDetails(null, null, "visa", "0002");

        var result = await RedeemAsync(link, receiptEmail: null);

        var saved = await ReloadPaymentAsync(result.Value.TenantPaymentId);
        Assert.Equal(PaymentStatus.Failed, saved.Status);
        Assert.Null(saved.Payer.ReceiptEmail);
        Assert.Equal("visa", saved.Payer.CardBrand);
        Assert.Equal("0002", saved.Payer.CardLast4);
    }

    [Fact]
    public async Task A_staff_charge_saves_the_receipt_email_and_discards_invalid_provider_data()
    {
        await SeedOfficeAsync();
        _stripe.Payer = new ProviderPayerDetails("Jane Doe", "not-an-email", "visa", "42");

        var result = await ChargeTenantPaymentHandler.Handle(
            new ChargeTenantPaymentCommand(
                _tenantId,
                PaymentProviderCode.Stripe,
                2500,
                "USD",
                TaxpayerId: null,
                PaymentPurposeKind.Other,
                PurposeExternalReferenceId: null,
                PaymentMethodReference: "pm_card_visa",
                ReceiptEmail: "client@example.com",
                IdempotencyKey: "staff-charge-payer",
                ActorUserId: Guid.NewGuid()
            ),
            new TenantPaymentRepository(_db),
            new TenantPaymentConfigRepository(_db),
            new TenantConnectAccountRepository(_db),
            InMemoryPaymentClient.FakeAdapters(_stripe),
            new PrefixSecretProtector(),
            Options.Create(
                new PlatformStripeCredentials { PlatformSecretKey = "unused", ConnectWebhookSecret = "unused" }
            ),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus([]),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<TenantPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var saved = await ReloadPaymentAsync(result.Value);
        Assert.Equal(PaymentStatus.Succeeded, saved.Status);
        Assert.Equal("client@example.com", saved.Payer.ReceiptEmail);
        Assert.Equal("Jane Doe", saved.Payer.PayerName);
        Assert.Null(saved.Payer.PayerEmail);
        Assert.Equal("visa", saved.Payer.CardBrand);
        Assert.Null(saved.Payer.CardLast4);
    }

    [Fact]
    public async Task The_payment_detail_returns_the_payer_fields()
    {
        var link = await SeedOfficeAsync();
        _stripe.Payer = new ProviderPayerDetails("Jane Doe", "jane@example.com", "visa", "4242");
        var redeemed = await RedeemAsync(link, receiptEmail: "client@example.com");

        var response = await GetDetailAsync(redeemed.Value.TenantPaymentId);

        Assert.Equal("client@example.com", response.ReceiptEmail);
        Assert.Equal("Jane Doe", response.PayerName);
        Assert.Equal("jane@example.com", response.PayerEmail);
        Assert.Equal("visa", response.CardBrand);
        Assert.Equal("4242", response.CardLast4);
    }

    [Fact]
    public async Task A_payment_without_payer_data_returns_nulls()
    {
        var link = await SeedOfficeAsync();
        var redeemed = await RedeemAsync(link, receiptEmail: null);

        var response = await GetDetailAsync(redeemed.Value.TenantPaymentId);

        Assert.Null(response.ReceiptEmail);
        Assert.Null(response.PayerName);
        Assert.Null(response.PayerEmail);
        Assert.Null(response.CardBrand);
        Assert.Null(response.CardLast4);
    }

    private async Task<TenantPaymentResponse> GetDetailAsync(Guid paymentId)
    {
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        var result = await GetTenantPaymentByIdHandler.Handle(
            new GetTenantPaymentByIdQuery(_tenantId, paymentId),
            new TenantPaymentRepository(fresh),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private Task<BuildingBlocks.Results.Result<RedeemPaymentLinkResponse>> RedeemAsync(
        PaymentLink link,
        string? receiptEmail
    ) =>
        RedeemPaymentLinkHandler.Handle(
            new RedeemPaymentLinkCommand(link.Token.Value, PaymentProviderCode.Stripe, "pm_card", receiptEmail),
            new PaymentLinkRepository(_db),
            new TenantPaymentRepository(_db),
            new TenantPaymentConfigRepository(_db),
            InMemoryPaymentClient.FakeAdapters(_stripe),
            new PrefixSecretProtector(),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus([]),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<PaymentLink>.Instance,
            CancellationToken.None
        );

    // Contexto nuevo: lo leído viene de la base, no de la instancia que quedó en memoria.
    private async Task<TenantPayment> ReloadPaymentAsync(Guid paymentId)
    {
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        return await fresh.TenantPayments.IgnoreQueryFilters().SingleAsync(payment => payment.Id == paymentId);
    }

    private async Task<PaymentLink> SeedOfficeAsync()
    {
        _db.Tenants.Add(
            Tenant.Register(_tenantId, "Acme Tax Office", "acme-tax", TenantKind.Customer, "Etc/UTC", NowUtc).Value
        );

        var stripe = TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_office",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                NowUtc
            )
            .Value;
        stripe.UpdateSecrets(Encrypted("sk_test_office"), Encrypted("whsec_office"), Guid.Empty, NowUtc);
        stripe.MarkActive(Guid.Empty, NowUtc);
        _db.TenantPaymentConfigs.Add(stripe);

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

    private static EncryptedSecret Encrypted(string plaintext) =>
        EncryptedSecret.Create(new PrefixSecretProtector().Protect(plaintext)).Value;
}
