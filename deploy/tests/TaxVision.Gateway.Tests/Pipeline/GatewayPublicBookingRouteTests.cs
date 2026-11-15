using System.Net;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace TaxVision.Gateway.Tests.Pipeline;

/// <summary>
/// La reserva pública de demos (F7) es anónima: el Gateway le pone un tope de body de 16 KB y el cupo pre-auth
/// por IP. Se prueba con la configuración real del Gateway (<c>appsettings.json</c>), YARP y Kestrel en loopback:
/// el tope solo lo aplica un servidor real, no el TestServer.
/// </summary>
public sealed class GatewayPublicBookingRouteTests
{
    private const int Cap = 16 * 1024;

    [Theory]
    [InlineData("/calendar/public/booking-pages/demo/bookings")]
    [InlineData("/calendar/public/bookings/verify")]
    [InlineData("/calendar/public/bookings/manage/cancel")]
    public async Task A_public_body_over_16_KB_is_rejected_with_413(string path)
    {
        await using var proxy = await Proxy.StartAsync(withRateLimiter: false);

        var over = await proxy.Client.PostAsync(path, Body(Cap + 1));
        var under = await proxy.Client.PostAsync(path, Body(Cap - 1));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, over.StatusCode);
        Assert.Equal(HttpStatusCode.OK, under.StatusCode);
    }

    [Fact]
    public async Task The_rest_of_calendar_keeps_the_default_body_size()
    {
        await using var proxy = await Proxy.StartAsync(withRateLimiter: false);

        var response = await proxy.Client.PostAsync("/calendar/appointments", Body(64 * 1024));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/calendar/public/booking-pages/demo/bookings")]
    [InlineData("/calendar/public/booking-pages/another-page/bookings")]
    [InlineData("/calendar/public/bookings/verify")]
    [InlineData("/calendar/public/bookings/choose-slot")]
    [InlineData("/calendar/public/bookings/resend")]
    [InlineData("/calendar/public/bookings/manage/view")]
    [InlineData("/calendar/public/bookings/manage/reschedule")]
    [InlineData("/calendar/public/bookings/manage/cancel")]
    public void Every_public_command_is_in_the_pre_auth_gate(string path)
    {
        var rules = PreAuthRules();

        Assert.Contains(rules, rule => rule.Matches(path, "POST"));
    }

    /// <summary>
    /// Las lecturas no entran: comparten un solo cupo de 30 por minuto y Calendar ya las limita a 60 por IP. Ver
    /// semanas de horarios no debe dar 429 antes que el servicio. Las reglas de Auth siguen.
    /// </summary>
    [Theory]
    [InlineData("/calendar/public/booking-pages/demo", "GET", false)]
    [InlineData("/calendar/public/booking-pages/demo/slots", "GET", false)]
    [InlineData("/calendar/appointments", "POST", false)]
    [InlineData("/auth/login", "POST", true)]
    [InlineData("/tenants", "POST", true)]
    public void Reads_and_staff_routes_stay_out_of_the_gate(string path, string method, bool gated)
    {
        var rules = PreAuthRules();

        Assert.Equal(gated, rules.Any(rule => rule.Matches(path, method)));
    }

    [Fact]
    public async Task The_gate_answers_429_after_30_commands_per_minute_from_one_ip()
    {
        await using var proxy = await Proxy.StartAsync(withRateLimiter: true);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 31; i++)
            statuses.Add((await proxy.Client.PostAsync("/calendar/public/bookings/resend", Body(10))).StatusCode);

        Assert.All(statuses.Take(30), status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[30]);
    }

    private static IReadOnlyList<GatewayRateLimitRule> PreAuthRules()
    {
        var services = new ServiceCollection();
        services.AddTaxVisionGatewayRateLimiting(GatewaySettings());
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<GatewayRateLimitOptions>>().Value.PreAuthByIp.Rules;
    }

    private static ByteArrayContent Body(int bytes) =>
        new(new byte[bytes]) { Headers = { { "Content-Type", "application/json" } } };

    private static IConfiguration GatewaySettings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir!.FullName, "src", "Gateway", "TaxVision.Gateway", "appsettings.json"))
            .Build();
    }

    /// <summary>Gateway mínimo: las rutas de Calendar y el limitador tal como están configurados, contra un eco.</summary>
    private sealed class Proxy : IAsyncDisposable
    {
        private readonly WebApplication _upstream;
        private readonly WebApplication _gateway;

        private Proxy(WebApplication upstream, WebApplication gateway, HttpClient client)
        {
            _upstream = upstream;
            _gateway = gateway;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<Proxy> StartAsync(bool withRateLimiter)
        {
            var upstream = Loopback(WebApplication.CreateBuilder());
            upstream.MapPost(
                "/{**path}",
                async (HttpContext context) =>
                {
                    await context.Request.Body.CopyToAsync(Stream.Null);
                    return Results.Ok();
                }
            );
            await upstream.StartAsync();

            var settings = GatewaySettings();
            var proxyConfig = new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:calendar:Destinations:calendar1:Address"] = Address(upstream),
            };
            foreach (var route in settings.GetSection("ReverseProxy:Routes").GetChildren())
            {
                if (route["ClusterId"] != "calendar")
                    continue;
                foreach (var (key, value) in route.AsEnumerable(makePathsRelative: true))
                    if (value is not null)
                        proxyConfig[$"ReverseProxy:Routes:{route.Key}:{key}"] = value;
            }

            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(proxyConfig);
            builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
            if (withRateLimiter)
                builder.Services.AddTaxVisionGatewayRateLimiting(settings);
            var gateway = Loopback(builder);
            if (withRateLimiter)
                gateway.UseRateLimiter();
            gateway.MapReverseProxy();
            await gateway.StartAsync();

            return new Proxy(upstream, gateway, new HttpClient { BaseAddress = new Uri(Address(gateway)) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _gateway.DisposeAsync();
            await _upstream.DisposeAsync();
        }

        private static WebApplication Loopback(WebApplicationBuilder builder)
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            return builder.Build();
        }

        private static string Address(WebApplication app) =>
            app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }
}
