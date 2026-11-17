using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Infrastructure.Growth;

namespace TaxVision.Subscription.Tests.Infrastructure;

/// <summary>
/// El canje llama a Growth con el token de su cliente propio (audiencia taxvision-growth y scopes de códigos), no con
/// el de servicio general. Los errores Codes.* pasan tal cual; una negativa de autorización es "no disponible".
/// </summary>
public sealed class GrowthCodeRedemptionClientTests
{
    private static readonly Guid Office = Guid.Parse("30000000-0000-0000-0000-000000000009");

    [Fact]
    public async Task Quote_uses_the_growth_codes_token_of_the_office()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, QuoteJson);
        var tokens = new FakeCodesTokens("codes-token");

        var quote = await Client(handler, tokens).QuoteAsync(Request(), CancellationToken.None);

        Assert.True(quote.IsSuccess, quote.IsFailure ? quote.Error.Code : null);
        Assert.Equal(Office, tokens.LastTenant);
        Assert.Equal("Bearer codes-token", handler.Authorization);
        Assert.Equal("/internal/codes/quotes", handler.Path);
        Assert.Equal("Months", quote.Value.Duration);
        Assert.Equal("SPRI...0420", quote.Value.CodeDisplay);
    }

    [Fact]
    public async Task Without_the_growth_client_the_redeem_is_unavailable()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, QuoteJson);

        var quote = await Client(handler, new FakeCodesTokens(null)).QuoteAsync(Request(), CancellationToken.None);

        Assert.Equal("Subscription.DiscountCode.Unavailable", quote.Error.Code);
        Assert.Null(handler.Path);
    }

    [Fact]
    public async Task A_growth_business_error_passes_through()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.NotFound,
            """{"code":"Codes.CreateQuote.CodeNotFound","message":"No applicable code was found."}"""
        );

        var quote = await Client(handler, new FakeCodesTokens("t")).QuoteAsync(Request(), CancellationToken.None);

        Assert.Equal("Codes.CreateQuote.CodeNotFound", quote.Error.Code);
    }

    [Fact]
    public async Task An_authorization_denial_is_unavailable_not_an_invalid_code()
    {
        var handler = new RecordingHandler(HttpStatusCode.Forbidden, """{"code":"Authz.PermissionDenied"}""");

        var quote = await Client(handler, new FakeCodesTokens("t")).QuoteAsync(Request(), CancellationToken.None);

        Assert.Equal("Subscription.DiscountCode.Unavailable", quote.Error.Code);
    }

    [Fact]
    public async Task Quote_sends_the_plan_and_the_offer_with_its_cycle()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, QuoteJson);
        var plan = Guid.Parse("50000000-0000-0000-0000-000000000001");

        await Client(handler, new FakeCodesTokens("t"))
            .QuoteAsync(Request() with { PlanId = plan, BillingCycle = "Yearly" }, CancellationToken.None);

        Assert.Contains(
            $"\"scopeTargets\":[{{\"type\":\"Plan\",\"scopeId\":\"{plan}\"}},"
                + $"{{\"type\":\"Offer\",\"scopeId\":\"Subscription/{plan}/Yearly\"}}]",
            handler.Body
        );
    }

    private const string QuoteJson = """
        {"quoteId":"40000000-0000-0000-0000-000000000001","currency":"USD",
         "codeDefinitionId":"40000000-0000-0000-0000-000000000002","percentageBasisPoints":2000,
         "fixedAmountCents":null,"duration":"Months","durationMonths":3,"codeDisplay":"SPRI...0420"}
        """;

    private static GrowthCodeQuoteRequest Request() =>
        new(Office, "STAY20", Guid.NewGuid(), 2_900, "USD", new string('a', 64), Guid.NewGuid());

    private static GrowthCodeRedemptionClient Client(HttpMessageHandler handler, IGrowthCodesTokenAcquirer tokens) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            tokens,
            NullLogger<GrowthCodeRedemptionClient>.Instance
        );

    private sealed class FakeCodesTokens(string? token) : IGrowthCodesTokenAcquirer
    {
        public Guid? LastTenant { get; private set; }

        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
        {
            LastTenant = tenantId;
            return Task.FromResult(token);
        }
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Path = request.RequestUri?.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
