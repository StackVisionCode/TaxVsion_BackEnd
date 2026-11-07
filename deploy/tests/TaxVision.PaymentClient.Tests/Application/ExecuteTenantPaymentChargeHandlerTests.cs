using System.Net;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Infrastructure.Providers.PayPal;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>D-PAY-13: si el proveedor no dijo si cobró, el pago no se da por fallido y lo cierra la reconciliación.</summary>
public sealed class ExecuteTenantPaymentChargeHandlerTests
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly Guid _tenantId = Guid.NewGuid();

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":{"type":"api_error","message":"boom"}}""")]
    [InlineData(HttpStatusCode.BadGateway, "bad gateway")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"type":"idempotency_error","message":"key reused"}}""")]
    [InlineData(
        HttpStatusCode.Conflict,
        """{"error":{"type":"invalid_request_error","code":"idempotency_key_in_use","message":"in use"}}"""
    )]
    public async Task Stripe_unknown_outcome_leaves_the_payment_for_reconciliation(HttpStatusCode status, string body)
    {
        await SeedConfigAsync(PaymentProviderCode.Stripe);

        var result = await ChargeAsync(StripeAdapter(status, body), PaymentProviderCode.Stripe, "pm_card");

        Assert.True(result.IsSuccess);
        await AssertPendingAsync(result.Value);
    }

    [Fact]
    public async Task Stripe_decline_is_a_failure()
    {
        await SeedConfigAsync(PaymentProviderCode.Stripe);

        var result = await ChargeAsync(
            StripeAdapter(
                HttpStatusCode.PaymentRequired,
                """{"error":{"type":"card_error","code":"card_declined","message":"declined"}}"""
            ),
            PaymentProviderCode.Stripe,
            "pm_card"
        );

        Assert.Equal(PaymentStatus.Failed, (await LoadAsync(result.Value)).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task PayPal_capture_without_a_readable_answer_leaves_the_payment_for_reconciliation(
        HttpStatusCode status,
        string body
    )
    {
        await SeedConfigAsync(PaymentProviderCode.PayPal);

        var result = await ChargeAsync(
            PayPalAdapter(new PayPalStub { Capture = (status, body) }),
            PaymentProviderCode.PayPal,
            "ORDER-1"
        );

        Assert.True(result.IsSuccess);
        await AssertPendingAsync(result.Value);
    }

    [Fact]
    public async Task PayPal_capture_timeout_leaves_the_payment_for_reconciliation()
    {
        await SeedConfigAsync(PaymentProviderCode.PayPal);

        var result = await ChargeAsync(
            PayPalAdapter(new PayPalStub { CaptureTimesOut = true }),
            PaymentProviderCode.PayPal,
            "ORDER-1"
        );

        Assert.True(result.IsSuccess);
        await AssertPendingAsync(result.Value);
    }

    [Fact]
    public async Task PayPal_capture_rejected_is_a_failure()
    {
        await SeedConfigAsync(PaymentProviderCode.PayPal);

        var result = await ChargeAsync(
            PayPalAdapter(
                new PayPalStub { Capture = (HttpStatusCode.UnprocessableEntity, """{"name":"UNPROCESSABLE_ENTITY"}""") }
            ),
            PaymentProviderCode.PayPal,
            "ORDER-1"
        );

        Assert.Equal(PaymentStatus.Failed, (await LoadAsync(result.Value)).Status);
    }

    private async Task AssertPendingAsync(Guid paymentId)
    {
        var payment = await LoadAsync(paymentId);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.FailureCode);
    }

    private async Task<TenantPayment> LoadAsync(Guid paymentId)
    {
        await using var db = InMemoryPaymentClient.CreateDb(_databaseName);
        return await db.TenantPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == paymentId);
    }

    private async Task<Result<Guid>> ChargeAsync(IPaymentProvider adapter, PaymentProviderCode code, string method)
    {
        await using var db = InMemoryPaymentClient.CreateDb(_databaseName);
        return await TestChargeFlow.ChargeAsync(
            new ChargeTenantPaymentCommand(
                _tenantId,
                code,
                2500,
                "USD",
                null,
                PaymentPurposeKind.Other,
                null,
                method,
                ReceiptEmail: null,
                $"direct-{Guid.NewGuid():N}",
                Guid.Empty
            ),
            new TenantPaymentRepository(db),
            new TenantPaymentConfigRepository(db),
            new TenantConnectAccountRepository(db),
            InMemoryPaymentClient.FakeAdapters(adapter),
            new PrefixSecretProtector(),
            Options.Create(
                new PlatformStripeCredentials { PlatformSecretKey = "unused", ConnectWebhookSecret = "unused" }
            ),
            new PaymentAuditLogWriter(db),
            db,
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<TenantPayment>.Instance,
            CancellationToken.None
        );
    }

    private async Task SeedConfigAsync(PaymentProviderCode code)
    {
        await using var db = InMemoryPaymentClient.CreateDb(_databaseName);
        var config = TenantPaymentConfig
            .Create(
                _tenantId,
                code,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_office",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                NowUtc
            )
            .Value;
        config.UpdateSecrets(
            EncryptedSecret.Create(new PrefixSecretProtector().Protect("sk_test_office")).Value,
            null,
            Guid.Empty,
            NowUtc
        );
        config.MarkActive(Guid.Empty, NowUtc);
        db.TenantPaymentConfigs.Add(config);
        await db.SaveChangesAsync();
    }

    private static StripePaymentAdapter StripeAdapter(HttpStatusCode status, string body) =>
        new(NullLogger<StripePaymentAdapter>.Instance, new StubStripeClients(status, body));

    private static PayPalPaymentAdapter PayPalAdapter(PayPalStub stub) =>
        new(new StubHttpClients(stub), NullLogger<PayPalPaymentAdapter>.Instance);

    private sealed class StubStripeClients(HttpStatusCode status, string body) : IStripeClientFactory
    {
        public IStripeClient Create(string secretKey) =>
            new StripeClient(
                secretKey,
                httpClient: new SystemNetHttpClient(
                    new HttpClient(new FixedResponse(status, body)),
                    maxNetworkRetries: 0
                )
            );
    }

    private sealed class FixedResponse(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
    }

    private sealed class StubHttpClients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>El token OAuth siempre sale bien; la captura responde lo que diga la prueba.</summary>
    private sealed class PayPalStub : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body) Capture { get; init; }
        public bool CaptureTimesOut { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var isToken = request.RequestUri!.AbsolutePath == "/v1/oauth2/token";
            if (!isToken && CaptureTimesOut)
                throw new TaskCanceledException("Simulated timeout.");

            var (status, body) = isToken ? (HttpStatusCode.OK, """{ "access_token": "A21AA" }""") : Capture;
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
        }
    }
}
