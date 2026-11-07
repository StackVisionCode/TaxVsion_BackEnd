using System.Net;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;
using PaymentLink = TaxVision.PaymentClient.Domain.PaymentLinks.PaymentLink;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Canje del enlace público: un intento vivo (3DS pendiente) no deja abrir otro cobro, y la
/// clave de idempotencia es fija por intento.</summary>
public sealed class RedeemPaymentLinkHandlerTests
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly PaymentClientDbContext _db;
    private readonly RecordingPaymentProvider _stripe = new(
        new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false)
    );
    private readonly List<object> _published = [];
    private readonly Guid _tenantId = Guid.NewGuid();
    private IPaymentProvider? _adapter;

    public RedeemPaymentLinkHandlerTests() => _db = InMemoryPaymentClient.CreateDb(_dbName);

    [Fact]
    public async Task Redeem_WhenPreviousAttemptRequiresAction_ReturnsLiveAttemptWithoutCallingProvider()
    {
        var link = await SeedAsync();
        _stripe.Status = PaymentStatus.RequiresAction;
        _stripe.NextActionUrl = "https://3ds.test/first";
        // Al recargar se consulta el intento en curso (solo lectura); sigue pendiente.
        _stripe.ChargeStatus = lookup =>
            Result.Success(
                new WebhookEventPayload(lookup.ProviderChargeReference, PaymentStatus.RequiresAction, null, null, null)
            );

        var first = await RedeemAsync(link, "card-token-1");
        var second = await RedeemAsync(link, "card-token-2");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.TenantPaymentId, second.Value.TenantPaymentId);
        Assert.Equal(nameof(PaymentStatus.RequiresAction), second.Value.Status);
        Assert.Equal("https://3ds.test/first", second.Value.NextActionUrl);
        Assert.Single(_stripe.Requests);
        Assert.Single(await _db.TenantPayments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Redeem_AfterDeclinedAttempt_UsesDeterministicIdempotencyKey()
    {
        var link = await SeedAsync();

        _stripe.Status = PaymentStatus.Failed;
        await RedeemAsync(link, "card-token-1");
        _stripe.Status = PaymentStatus.Succeeded;
        await RedeemAsync(link, "card-token-2");

        Assert.Collection(
            _stripe.Requests,
            r => Assert.Equal($"paymentlink-redeem:{link.Id:N}:attempt:1", r.IdempotencyKey.Value),
            r => Assert.Equal($"paymentlink-redeem:{link.Id:N}:attempt:2", r.IdempotencyKey.Value)
        );
    }

    [Fact]
    public async Task Redeem_RequiresAction_ReturnsClientSecretWithoutStoringIt()
    {
        // Stripe pide 3DS con Stripe.js: la página necesita el clientSecret, pero no se guarda en la base.
        var link = await SeedAsync();
        _stripe.Status = PaymentStatus.RequiresAction;
        _stripe.ClientSecret = "pi_1_secret_first";

        var result = await RedeemAsync(link, "card-token-1");

        Assert.Equal(nameof(PaymentStatus.RequiresAction), result.Value.Status);
        Assert.Equal("use_stripe_sdk", result.Value.NextActionType);
        Assert.Equal("pi_1_secret_first", result.Value.ClientSecret);
        var audit = await _db.AuditEntries.IgnoreQueryFilters().ToListAsync();
        Assert.DoesNotContain(audit, a => (a.AfterPayload ?? string.Empty).Contains("secret"));
    }

    [Fact]
    public async Task Redeem_ReloadWithPendingThreeDs_ReturnsFreshClientSecretWithoutCharging()
    {
        // El cliente recarga con el 3DS pendiente: se pide a Stripe un clientSecret nuevo, sin cobrar de nuevo.
        var link = await SeedAsync();
        _stripe.Status = PaymentStatus.RequiresAction;
        _stripe.ClientSecret = "pi_1_secret_first";
        var first = await RedeemAsync(link, "card-token-1");
        _stripe.ChargeStatus = lookup =>
            Result.Success(
                new WebhookEventPayload(
                    lookup.ProviderChargeReference,
                    PaymentStatus.RequiresAction,
                    null,
                    null,
                    null,
                    NextActionClientSecret: "pi_1_secret_fresh"
                )
            );

        var second = await RedeemAsync(link, "card-token-2");

        Assert.Equal(first.Value.TenantPaymentId, second.Value.TenantPaymentId);
        Assert.Equal("pi_1_secret_fresh", second.Value.ClientSecret);
        Assert.Single(_stripe.Requests);
        var (credentials, lookup) = Assert.Single(_stripe.StatusLookups);
        Assert.Equal("sk_test_office", credentials.SecretKey);
        Assert.Equal("Stripe-charge-1", lookup.ProviderChargeReference);
    }

    // D-PAY-13: el cargo pudo salir. Para el pagador es un intento en curso y el link no acepta otro.
    [Fact]
    public async Task Redeem_ProviderOutcomeUnknown_KeepsTheAttemptOpen()
    {
        var link = await SeedAsync();
        _adapter = new StripePaymentAdapter(NullLogger<StripePaymentAdapter>.Instance, new ServerErrorStripeClients());

        var first = await RedeemAsync(link, "card-token-1");
        var second = await RedeemAsync(link, "card-token-2");

        Assert.Equal(PaymentLink.AttemptInProgressError, first.Error);
        Assert.Equal(PaymentLink.AttemptInProgressError, second.Error);
        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var payment = await read.TenantPayments.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public async Task Redeem_SaveFails_PublishesNothing()
    {
        // Si el guardado falla, no puede salir ni "link usado" ni "pagado" del intento revertido.
        var link = await SeedAsync();
        _stripe.Status = PaymentStatus.Succeeded;

        await Assert.ThrowsAsync<DbUpdateException>(() => RedeemAsync(link, "card-token-1", new FailingUnitOfWork()));

        Assert.Empty(_published);
    }

    /// <summary>Cada canje con su propio contexto, como en una petición HTTP real.</summary>
    private async Task<Result<RedeemPaymentLinkResponse>> RedeemAsync(
        PaymentLink link,
        string cardToken,
        IUnitOfWork? unitOfWork = null
    )
    {
        await using var db = InMemoryPaymentClient.CreateDb(_dbName);
        return await TestChargeFlow.RedeemAsync(
            new RedeemPaymentLinkCommand(link.Token.Value, PaymentProviderCode.Stripe, cardToken, ReceiptEmail: null),
            new PaymentLinkRepository(db),
            new TenantPaymentRepository(db),
            new TenantPaymentConfigRepository(db),
            InMemoryPaymentClient.FakeAdapters(_adapter ?? _stripe),
            new PrefixSecretProtector(),
            new PaymentAuditLogWriter(db),
            unitOfWork ?? db,
            InMemoryPaymentClient.RecordingBus(_published),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<PaymentLink>.Instance,
            CancellationToken.None
        );
    }

    private async Task<PaymentLink> SeedAsync()
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
        stripe.UpdateSecrets(
            EncryptedSecret.Create(new PrefixSecretProtector().Protect("sk_test_office")).Value,
            null,
            Guid.Empty,
            NowUtc
        );
        stripe.MarkActive(Guid.Empty, NowUtc);
        _db.TenantPaymentConfigs.Add(stripe);

        var link = PaymentLink
            .Create(
                _tenantId,
                null,
                Money.Create(2500, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-001").Value,
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

    private sealed class ServerErrorStripeClients : IStripeClientFactory
    {
        public IStripeClient Create(string secretKey) =>
            new StripeClient(
                secretKey,
                httpClient: new SystemNetHttpClient(new HttpClient(new ServerError()), maxNetworkRetries: 0)
            );

        private sealed class ServerError : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            throw new DbUpdateException("Simulated save failure.");
    }
}
