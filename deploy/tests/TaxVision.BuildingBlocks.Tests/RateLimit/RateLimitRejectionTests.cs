using System.Text.Json;
using System.Threading.RateLimiting;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// Contrato único del 429 (tiered, limiters nativos y Gateway). Antes los limiters nativos devolvían
/// un 429 vacío y el mensaje del tiered era técnico ("user rate limit exceeded…"): el front mostraba
/// texto crudo y no sabía cuánto esperar.
/// </summary>
// OnRejected emite ratelimit.native_rejected_total en el meter compartido: en paralelo ensuciaría los
// MeterListener de RateLimitMetricsTests.
[Collection(RateLimitMetricsCollection.Name)]
public sealed class RateLimitRejectionTests
{
    private sealed class RejectedLease(TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is not null && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = retryAfter.Value;
                return true;
            }

            metadata = null;
            return false;
        }
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonElement ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement.Clone();
    }

    [Fact]
    public async Task WriteAsync_Responde429ConRetryAfterYElBodyDelContrato()
    {
        var context = NewContext();

        await RateLimitRejection.WriteAsync(context, 30, "customer.h.search", "user");

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("30", context.Response.Headers.RetryAfter.ToString());

        var body = ReadBody(context);
        Assert.Equal("RateLimit.Exceeded", body.GetProperty("code").GetString());
        Assert.Equal(
            "You're making requests too quickly. Please try again in 30 seconds.",
            body.GetProperty("message").GetString()
        );
        Assert.Equal(30, body.GetProperty("retryAfterSeconds").GetInt32());
        Assert.Equal("customer.h.search", body.GetProperty("policy").GetString());
        Assert.Equal("user", body.GetProperty("layer").GetString());
    }

    [Fact]
    public async Task WriteAsync_SinPolicyNiLayer_LosOmiteDelBody()
    {
        var context = NewContext();

        await RateLimitRejection.WriteAsync(context, 5);

        var body = ReadBody(context);
        Assert.False(body.TryGetProperty("policy", out _));
        Assert.False(body.TryGetProperty("layer", out _));
    }

    [Theory]
    [InlineData(0, 1, "1 second")]
    [InlineData(-3, 1, "1 second")]
    [InlineData(1, 1, "1 second")]
    [InlineData(2, 2, "2 seconds")]
    public async Task WriteAsync_NuncaPideEsperarMenosDeUnSegundo(int requested, int expected, string phrase)
    {
        var context = NewContext();

        await RateLimitRejection.WriteAsync(context, requested);

        Assert.Equal(expected.ToString(), context.Response.Headers.RetryAfter.ToString());
        Assert.Contains($"try again in {phrase}.", ReadBody(context).GetProperty("message").GetString());
    }

    [Fact]
    public async Task OnRejected_UsaLaEsperaRealDelLeaseYLaPoliticaDelEndpoint()
    {
        var context = NewContext();
        context.SetEndpoint(
            new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new EnableRateLimitingAttribute("share-public")),
                "test"
            )
        );

        await RateLimitRejection.OnRejected(
            new OnRejectedContext { HttpContext = context, Lease = new RejectedLease(TimeSpan.FromSeconds(12.3)) },
            CancellationToken.None
        );

        Assert.Equal("13", context.Response.Headers.RetryAfter.ToString());
        var body = ReadBody(context);
        Assert.Equal(13, body.GetProperty("retryAfterSeconds").GetInt32());
        Assert.Equal("share-public", body.GetProperty("policy").GetString());
    }

    [Fact]
    public async Task OnRejected_SinEndpoint_TomaLaPoliticaDeItems()
    {
        var context = NewContext();
        context.Items[RateLimitRejection.PolicyItemKey] = "gateway.pre_auth_by_ip";

        await RateLimitRejection.OnRejected(
            new OnRejectedContext { HttpContext = context, Lease = new RejectedLease(TimeSpan.FromSeconds(40)) },
            CancellationToken.None
        );

        Assert.Equal("gateway.pre_auth_by_ip", ReadBody(context).GetProperty("policy").GetString());
    }

    [Fact]
    public async Task OnRejected_SinMetadataDeEspera_InformaElDefault()
    {
        var context = NewContext();

        await RateLimitRejection.OnRejected(
            new OnRejectedContext { HttpContext = context, Lease = new RejectedLease(null) },
            CancellationToken.None
        );

        Assert.Equal(
            RateLimitRejection.DefaultRetryAfterSeconds.ToString(),
            context.Response.Headers.RetryAfter.ToString()
        );
    }

    [Fact]
    public void UseTaxVisionRejectionResponse_ConfiguraStatusYHandler()
    {
        var options = new RateLimiterOptions().UseTaxVisionRejectionResponse();

        Assert.Equal(StatusCodes.Status429TooManyRequests, options.RejectionStatusCode);
        Assert.NotNull(options.OnRejected);
    }
}
