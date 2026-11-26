using BuildingBlocks.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Gateway.Middleware;
using Xunit;
using Yarp.ReverseProxy.Transforms;

namespace TaxVision.Gateway.Tests.Pipeline;

/// <summary>
/// Lo que recibe cada servicio detrás del Gateway: un solo CF-Connecting-IP con la IP ya resuelta, nunca el
/// valor que mandó el cliente. YARP y Kestrel reales en loopback, contra un eco que devuelve el header.
/// </summary>
public sealed class GatewayClientIpForwardingTests
{
    private const string Header = "CF-Connecting-IP";

    [Fact]
    public async Task A_trusted_neighbour_forwards_the_resolved_client_ip()
    {
        await using var proxy = await Proxy.StartAsync();

        Assert.Equal(["203.0.113.7"], await proxy.ForwardedAsync("203.0.113.7"));
    }

    [Fact]
    public async Task A_spoofed_header_from_an_untrusted_neighbour_is_replaced_by_the_socket_ip()
    {
        await using var proxy = await Proxy.StartAsync(new() { ["ReverseProxyTrust:KnownNetworks:0"] = "10.0.0.0/8" });

        Assert.Equal(["127.0.0.1"], await proxy.ForwardedAsync("203.0.113.7"));
    }

    [Fact]
    public async Task Without_a_header_the_service_still_gets_the_socket_ip()
    {
        await using var proxy = await Proxy.StartAsync();

        Assert.Equal(["127.0.0.1"], await proxy.ForwardedAsync());
    }

    [Theory]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    public async Task Ipv6_is_kept_and_ipv4_mapped_becomes_ipv4(string sent, string expected)
    {
        await using var proxy = await Proxy.StartAsync();

        Assert.Equal([expected], await proxy.ForwardedAsync(sent));
    }

    [Fact]
    public async Task Repeated_headers_from_the_client_reach_the_service_as_one_value()
    {
        await using var proxy = await Proxy.StartAsync(new() { ["ReverseProxyTrust:KnownNetworks:0"] = "10.0.0.0/8" });

        Assert.Equal(["127.0.0.1"], await proxy.ForwardedAsync("203.0.113.7", "198.51.100.2"));
    }

    /// <summary>Gateway mínimo: la pieza de IP compartida y el transform real del Gateway, contra un eco.</summary>
    private sealed class Proxy : IAsyncDisposable
    {
        private readonly WebApplication _upstream;
        private readonly WebApplication _gateway;
        private readonly HttpClient _client;

        private Proxy(WebApplication upstream, WebApplication gateway, HttpClient client)
        {
            _upstream = upstream;
            _gateway = gateway;
            _client = client;
        }

        public static async Task<Proxy> StartAsync(Dictionary<string, string?>? trust = null)
        {
            var upstream = Loopback(WebApplication.CreateBuilder());
            upstream.MapGet(
                "/{**path}",
                (HttpContext context) => string.Join("|", context.Request.Headers[Header].ToArray())
            );
            await upstream.StartAsync();

            var settings = new Dictionary<string, string?>(trust ?? [])
            {
                ["ReverseProxy:Routes:all:ClusterId"] = "echo",
                ["ReverseProxy:Routes:all:Match:Path"] = "/{**catch-all}",
                ["ReverseProxy:Clusters:echo:Destinations:echo1:Address"] = Address(upstream),
            };
            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.AddTaxVisionClientIpForwarding(builder.Configuration);
            builder
                .Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
                .AddTransforms(context => context.AddRequestTransform(ClientIpProxyTransform.Apply));
            var gateway = Loopback(builder);
            gateway.UseTaxVisionClientIp();
            gateway.MapReverseProxy();
            await gateway.StartAsync();

            return new Proxy(upstream, gateway, new HttpClient { BaseAddress = new Uri(Address(gateway)) });
        }

        public async Task<string[]> ForwardedAsync(params string[] values)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/echo");
            foreach (var value in values)
                request.Headers.TryAddWithoutValidation(Header, value);
            using var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            return body.Split('|', StringSplitOptions.RemoveEmptyEntries);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
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
