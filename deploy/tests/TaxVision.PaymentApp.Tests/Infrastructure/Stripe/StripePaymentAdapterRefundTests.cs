using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Infrastructure.Stripe;

/// <summary>El reembolso viaja a Stripe con la clave de idempotencia de la solicitud.</summary>
public sealed class StripePaymentAdapterRefundTests
{
    [Fact]
    public async Task Refund_sends_idempotency_key()
    {
        var handler = new CapturingHandler();
        var client = new StripeClient("sk_test_dummy", httpClient: new SystemNetHttpClient(new HttpClient(handler)));
        var adapter = new StripePaymentAdapter(
            client,
            NullLogger<StripePaymentAdapter>.Instance,
            new FakePaymentAppMetrics()
        );

        var result = await adapter.RefundAsync(
            "pi_paid",
            Money.Create(1000, "USD").Value,
            "customer request",
            "refund:abc:0:1000",
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/refunds", request.RequestUri!.AbsolutePath);
        Assert.Equal("refund:abc:0:1000", request.Headers.GetValues("Idempotency-Key").Single());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"id":"re_1","object":"refund","status":"succeeded","amount":1000,"currency":"usd"}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
