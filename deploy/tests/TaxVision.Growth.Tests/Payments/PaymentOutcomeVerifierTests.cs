using System.Net;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Infrastructure;

namespace TaxVision.Growth.Tests.Payments;

/// <summary>GRW-1 paso 4: el verificador del host pregunta a PaymentApp (M2M, solo lectura) si el pago de la reserva
/// está confirmado. Se resuelve desde la DI real; solo el transporte HTTP es falso.</summary>
public sealed class PaymentOutcomeVerifierTests
{
    private const string Snapshot = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Confirmed_PaymentApp_payment_authorizes_the_late_commit()
    {
        var http = new StubPaymentApp(
            """{"found":true,"confirmed":true,"status":"Succeeded","promotionSnapshotHash":null}"""
        );
        var reservationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var result = await VerifyAsync(http, PaymentApp(paymentId), Subject(SubjectType.Tenant, "t1"), reservationId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var query = Assert.Single(http.OutcomeRequests);
        Assert.Contains("paymentSource=PaymentApp", query.Query);
        Assert.Contains($"paymentId={paymentId}", query.Query);
        Assert.Contains($"codeReservationId={reservationId}", query.Query);
        Assert.Equal("Bearer", http.OutcomeAuthorization);
    }

    [Fact]
    public async Task Onboarding_reservation_is_verified_by_its_onboarding()
    {
        var http = new StubPaymentApp("""{"found":true,"confirmed":true,"status":"Succeeded"}""");
        var onboardingId = Guid.NewGuid();

        var result = await VerifyAsync(
            http,
            Reference("Onboarding", Guid.NewGuid()),
            Subject(SubjectType.Anonymous, onboardingId.ToString("N"))
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Contains($"onboardingId={onboardingId}", Assert.Single(http.OutcomeRequests).Query);
    }

    [Fact]
    public async Task Payment_not_confirmed_is_denied_for_review()
    {
        var http = new StubPaymentApp("""{"found":true,"confirmed":false,"status":"Refunded"}""");

        var result = await VerifyAsync(http, PaymentApp(Guid.NewGuid()), Subject(SubjectType.Tenant, "t1"));

        Assert.Equal("Codes.PaymentOutcomeVerifier.NotConfirmed", result.Error.Code);
    }

    [Fact]
    public async Task Different_promotion_snapshot_is_not_confirmed()
    {
        var http = new StubPaymentApp(
            """{"found":true,"confirmed":true,"status":"Succeeded","promotionSnapshotHash":"other"}"""
        );

        var result = await VerifyAsync(http, PaymentApp(Guid.NewGuid()), Subject(SubjectType.Tenant, "t1"));

        Assert.Equal("Codes.PaymentOutcomeVerifier.NotConfirmed", result.Error.Code);
    }

    [Fact]
    public async Task PaymentApp_down_is_transient_not_a_review()
    {
        var http = new StubPaymentApp(status: HttpStatusCode.ServiceUnavailable);

        var result = await VerifyAsync(http, PaymentApp(Guid.NewGuid()), Subject(SubjectType.Tenant, "t1"));

        Assert.Equal("Codes.PaymentOutcomeVerifier.Unavailable", result.Error.Code);
    }

    [Fact]
    public async Task Disabled_verifier_does_not_call_PaymentApp()
    {
        var http = new StubPaymentApp("""{"found":true,"confirmed":true}""");

        var result = await VerifyAsync(
            http,
            PaymentApp(Guid.NewGuid()),
            Subject(SubjectType.Tenant, "t1"),
            enabled: false
        );

        Assert.Equal("Codes.PaymentOutcomeVerifier.Disabled", result.Error.Code);
        Assert.Empty(http.OutcomeRequests);
    }

    private static async Task<Result> VerifyAsync(
        StubPaymentApp http,
        PaymentReference payment,
        SubjectReference subject,
        Guid? reservationId = null,
        bool enabled = true
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=unused;Database=unused",
                    ["Growth:PaymentOutcomeVerifier:Enabled"] = enabled.ToString(),
                    ["Growth:PaymentOutcomeVerifier:PaymentAppBaseUrl"] = "http://payment-app.test",
                    ["ServiceAuthClient:AuthBaseUrl"] = "http://auth.test",
                    ["ServiceAuthClient:ClientId"] = "growth-test",
                    ["ServiceAuthClient:ClientSecret"] = "not-a-real-secret",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrowthInfrastructure(configuration);
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = http)
        );
        await using var provider = services.BuildServiceProvider();
        var verifier = provider.GetRequiredService<IPaymentOutcomeVerifier>();

        return await verifier.VerifySucceededAsync(
            new PaymentOutcomeQuery(
                Guid.NewGuid(),
                reservationId ?? Guid.NewGuid(),
                payment,
                subject,
                Guid.NewGuid(),
                SnapshotHash.Create(Snapshot).Value
            )
        );
    }

    private static PaymentReference PaymentApp(Guid paymentId) => Reference("PaymentApp", paymentId);

    private static PaymentReference Reference(string source, Guid paymentId) =>
        PaymentReference.Create(source, paymentId).Value;

    private static SubjectReference Subject(SubjectType type, string id) => SubjectReference.Create(type, id).Value;

    /// <summary>Auth entrega un token de servicio; PaymentApp responde la consulta del pago.</summary>
    private sealed class StubPaymentApp(string? outcomeJson = null, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public List<Uri> OutcomeRequests { get; } = [];
        public string? OutcomeAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("auth/service-token", StringComparison.Ordinal))
                return Json("""{"accessToken":"service-token","expiresInSeconds":3600}""");

            OutcomeRequests.Add(request.RequestUri);
            OutcomeAuthorization = request.Headers.Authorization?.Scheme;
            return status == HttpStatusCode.OK
                ? Json(outcomeJson ?? "{}")
                : Task.FromResult(new HttpResponseMessage(status));
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
    }
}
