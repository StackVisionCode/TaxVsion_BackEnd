using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BuildingBlocks.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Hosting;

/// <summary>
/// Comportamiento real de la pieza compartida de IP (Kestrel en loopback, no TestServer): la IP de
/// CF-Connecting-IP solo se adopta si el vecino inmediato es de confianza. Complementa a
/// <c>ClientIpForwardingFitnessTests</c>, que solo comprueba que cada host la registre.
/// </summary>
public sealed class ClientIpForwardingBehaviourTests
{
    private const string Header = "CF-Connecting-IP";

    [Fact]
    public async Task A_trusted_neighbour_sets_the_client_ip_from_the_header()
    {
        await using var host = await Host.StartAsync();

        Assert.Equal("203.0.113.7", await host.ResolvedIpAsync("203.0.113.7"));
    }

    [Fact]
    public async Task An_untrusted_neighbour_cannot_spoof_the_client_ip()
    {
        // Solo 10.0.0.0/8 es de confianza: la conexión desde 127.0.0.1 no lo es.
        await using var host = await Host.StartAsync(new() { ["ReverseProxyTrust:KnownNetworks:0"] = "10.0.0.0/8" });

        Assert.Equal("127.0.0.1", await host.ResolvedIpAsync("203.0.113.7"));
    }

    // Con las listas vacías ASP.NET no comprueba al vecino y confía en cualquiera: "false" no debe dejarlas vacías.
    [Fact]
    public void Without_private_networks_by_default_only_loopback_is_trusted()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["ReverseProxyTrust:TrustPrivateNetworksByDefault"] = "false" }
            )
            .Build();
        services.AddTaxVisionClientIpForwarding(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Empty(options.KnownProxies);
        Assert.Equal(["127.0.0.0/8", "::1/128"], options.KnownIPNetworks.Select(network => network.ToString()));
    }

    [Fact]
    public async Task Without_private_networks_by_default_a_private_neighbour_cannot_spoof_the_client_ip()
    {
        var lanIp = LocalPrivateIpv4();
        if (lanIp is null)
            return;
        await using var strict = await Host.StartAsync(
            new() { ["ReverseProxyTrust:TrustPrivateNetworksByDefault"] = "false" },
            bindIp: lanIp
        );
        await using var byDefault = await Host.StartAsync(bindIp: lanIp);

        Assert.Equal(lanIp, await strict.ResolvedIpAsync("203.0.113.7"));
        // Control: con el valor por defecto el mismo vecino privado sí es de confianza.
        Assert.Equal("203.0.113.7", await byDefault.ResolvedIpAsync("203.0.113.7"));
    }

    /// <summary>Una IPv4 privada de esta máquina (no loopback), para conectarse a uno mismo como "vecino privado".</summary>
    private static string? LocalPrivateIpv4() =>
        NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            .Select(ip => ip.ToString())
            .FirstOrDefault(ip => ip.StartsWith("10.") || ip.StartsWith("192.168.") || ip.StartsWith("172."));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-ip")]
    public async Task A_missing_or_unreadable_header_keeps_the_socket_ip(string? value)
    {
        await using var host = await Host.StartAsync();

        Assert.Equal("127.0.0.1", await host.ResolvedIpAsync(value));
    }

    [Fact]
    public async Task An_ipv6_client_ip_is_kept_as_ipv6()
    {
        await using var host = await Host.StartAsync();

        Assert.Equal("2001:db8::1", await host.ResolvedIpAsync("2001:db8::1"));
    }

    [Fact]
    public async Task An_ipv6_loopback_neighbour_is_trusted()
    {
        if (!Socket.OSSupportsIPv6)
            return;
        await using var host = await Host.StartAsync(ipv6: true);

        Assert.Equal("203.0.113.7", await host.ResolvedIpAsync("203.0.113.7"));
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Host(WebApplication app, HttpClient client)
        {
            _app = app;
            _client = client;
        }

        public static async Task<Host> StartAsync(
            Dictionary<string, string?>? settings = null,
            bool ipv6 = false,
            string? bindIp = null
        )
        {
            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(settings ?? []);
            builder.Services.AddTaxVisionClientIpForwarding(builder.Configuration);
            builder.WebHost.UseUrls(
                bindIp is not null ? $"http://{bindIp}:0"
                : ipv6 ? "http://[::1]:0"
                : "http://127.0.0.1:0"
            );
            builder.Logging.ClearProviders();
            var app = builder.Build();
            app.UseTaxVisionClientIp();
            app.MapGet("/ip", (HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "");
            await app.StartAsync();

            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            return new Host(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public async Task<string> ResolvedIpAsync(string? headerValue)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/ip");
            if (headerValue is not null)
                request.Headers.TryAddWithoutValidation(Header, headerValue);
            using var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.DisposeAsync();
        }
    }
}
