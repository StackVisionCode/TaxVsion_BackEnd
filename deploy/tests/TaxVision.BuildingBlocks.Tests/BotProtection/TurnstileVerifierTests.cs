using System.Net;
using System.Text;
using BuildingBlocks.Web.BotProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.BotProtection;

/// <summary>Turnstile verificado en el servidor: todo lo que no sea un sí explícito de Cloudflare es un no.</summary>
public sealed class TurnstileVerifierTests
{
    private static TurnstileOptions Ready(bool allowTestingKeys = false, params string[] hostnames) =>
        new()
        {
            SiteKey = "site-key",
            SecretKey = "secret-key",
            ExpectedAction = "public_booking",
            AllowTestingKeys = allowTestingKeys,
            ExpectedHostnames = [.. hostnames],
        };

    [Fact]
    public async Task Without_configuration_it_is_not_ready_and_never_calls_cloudflare()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"success":true,"action":"public_booking"}""");
        var verifier = Create(new TurnstileOptions { ExpectedAction = "public_booking" }, handler);

        Assert.False(verifier.IsReady);
        Assert.Null(verifier.SiteKey);
        Assert.Equal(TurnstileOutcome.NotConfigured, await verifier.VerifyAsync("token", "203.0.113.7"));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_valid_token_for_the_expected_action_passes()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """{"success":true,"action":"public_booking","hostname":"taxoffix.io"}"""
        );
        var verifier = Create(Ready(), handler);

        var outcome = await verifier.VerifyAsync("token", "203.0.113.7");

        Assert.Equal(TurnstileOutcome.Passed, outcome);
        Assert.Equal("site-key", verifier.SiteKey);
        Assert.Contains("secret=secret-key", handler.LastBody);
        Assert.Contains("response=token", handler.LastBody);
        Assert.Contains("remoteip=203.0.113.7", handler.LastBody);
        Assert.Contains("idempotency_key=", handler.LastBody);
    }

    [Theory]
    [InlineData("""{"success":false,"error-codes":["invalid-input-response"]}""")]
    [InlineData("""{"success":true,"action":"client_forms_intake"}""")]
    [InlineData("""{"success":true}""")]
    [InlineData("not json")]
    public async Task A_failed_check_or_another_action_is_rejected(string body)
    {
        var verifier = Create(Ready(), new StubHandler(HttpStatusCode.OK, body));

        Assert.Equal(TurnstileOutcome.Rejected, await verifier.VerifyAsync("token", "203.0.113.7"));
    }

    [Fact]
    public async Task Testing_keys_only_pass_where_they_are_allowed()
    {
        const string testing = """{"success":true,"metadata":{"result_with_testing_key":true}}""";

        Assert.Equal(
            TurnstileOutcome.Passed,
            await Create(Ready(allowTestingKeys: true), new StubHandler(HttpStatusCode.OK, testing))
                .VerifyAsync("t", "ip")
        );
        Assert.Equal(
            TurnstileOutcome.Rejected,
            await Create(Ready(), new StubHandler(HttpStatusCode.OK, testing)).VerifyAsync("t", "ip")
        );
    }

    [Fact]
    public async Task A_token_from_another_hostname_is_rejected_when_hostnames_are_set()
    {
        var body = """{"success":true,"action":"public_booking","hostname":"evil.example"}""";

        Assert.Equal(
            TurnstileOutcome.Rejected,
            await Create(Ready(false, "taxoffix.io"), new StubHandler(HttpStatusCode.OK, body)).VerifyAsync("t", "ip")
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_missing_token_is_rejected_without_calling_cloudflare(string? token)
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"success":true,"action":"public_booking"}""");

        Assert.Equal(TurnstileOutcome.Rejected, await Create(Ready(), handler).VerifyAsync(token, "ip"));
        Assert.Equal(
            TurnstileOutcome.Rejected,
            await Create(Ready(), handler).VerifyAsync(new string('x', 2049), "ip")
        );
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task When_cloudflare_fails_the_answer_is_unavailable_never_passed()
    {
        Assert.Equal(
            TurnstileOutcome.Unavailable,
            await Create(Ready(), new StubHandler(HttpStatusCode.InternalServerError, "")).VerifyAsync("t", "ip")
        );
        Assert.Equal(
            TurnstileOutcome.Unavailable,
            await Create(Ready(), new StubHandler(new HttpRequestException("down"))).VerifyAsync("t", "ip")
        );
        Assert.Equal(
            TurnstileOutcome.Unavailable,
            await Create(Ready(), new StubHandler(new TaskCanceledException("timeout"))).VerifyAsync("t", "ip")
        );
    }

    [Fact]
    public void The_registration_binds_the_section_of_the_service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Calendar:PublicBooking:Turnstile:SiteKey"] = "site",
                    ["Calendar:PublicBooking:Turnstile:SecretKey"] = "secret",
                    ["Calendar:PublicBooking:Turnstile:ExpectedAction"] = "public_booking",
                }
            )
            .Build();
        var services = new ServiceCollection();

        services.AddTurnstileVerification(configuration.GetSection("Calendar:PublicBooking:Turnstile"));
        using var provider = services.BuildServiceProvider();

        var verifier = provider.GetRequiredService<ITurnstileVerifier>();
        Assert.True(verifier.IsReady);
        Assert.Equal("site", verifier.SiteKey);
    }

    private static TurnstileVerifier Create(TurnstileOptions options, StubHandler handler) =>
        new(new HttpClient(handler), Options.Create(options));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body = string.Empty;
        private readonly Exception? _error;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public StubHandler(Exception error) => _error = error;

        public int Calls { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            if (_error is not null)
                throw _error;
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
