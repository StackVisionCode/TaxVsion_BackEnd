using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Gateway.Middleware;
using Xunit;

namespace TaxVision.Gateway.Tests.Middleware;

/// <summary>
/// El Gateway no escucha el bus, así que un renombre de oficina o un dominio deshabilitado solo se nota
/// cuando la cache host→tenant vuelve a preguntarle a Auth. Antes eso era a los 15 minutos: el host viejo
/// seguía entrando y, si otra oficina tomaba ese slug, sus usuarios recibían 403 todo ese rato.
/// </summary>
public sealed class HostTenantResolverTests
{
    private static readonly Guid OldTenant = Guid.NewGuid();
    private static readonly Guid NewTenant = Guid.NewGuid();

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Auth de mentira: contesta lo que diga <see cref="Next"/> y cuenta las llamadas.</summary>
    private sealed class FakeAuth : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Next { get; set; } = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Next());
        }
    }

    private static HttpResponseMessage TenantIs(Guid id) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"tenantId\":\"{id}\"}}", Encoding.UTF8, "application/json"),
        };

    private static (HostTenantResolver Resolver, FakeAuth Auth, ManualClock Clock) Build(HostTenantCache? cache = null)
    {
        var auth = new FakeAuth();
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        var resolver = new HostTenantResolver(
            new HttpClient(auth) { BaseAddress = new Uri("http://auth/") },
            cache ?? new HostTenantCache(),
            Options.Create(new TenantHostGuardOptions()),
            clock,
            NullLogger<HostTenantResolver>.Instance
        );
        return (resolver, auth, clock);
    }

    private static async Task WaitForCallsAsync(FakeAuth auth, int expected)
    {
        for (var i = 0; i < 100 && Volatile.Read(ref auth.Calls) < expected; i++)
            await Task.Delay(10);
    }

    [Fact]
    public async Task Una_resolucion_reciente_se_sirve_de_cache_sin_volver_a_Auth()
    {
        var (resolver, auth, clock) = Build();
        auth.Next = () => TenantIs(OldTenant);

        await resolver.ResolveAsync("acme.taxoffix.io");
        clock.Now += TimeSpan.FromSeconds(10);
        var second = await resolver.ResolveAsync("ACME.taxoffix.io");

        Assert.Equal(OldTenant, second.TenantId);
        Assert.Equal(1, auth.Calls);
    }

    [Fact]
    public async Task Pasado_el_umbral_sirve_la_cacheada_y_la_revalida_en_segundo_plano()
    {
        var (resolver, auth, clock) = Build();
        auth.Next = () => TenantIs(OldTenant);
        await resolver.ResolveAsync("acme.taxoffix.io");

        // El slug cambió de dueño en Auth.
        auth.Next = () => TenantIs(NewTenant);
        clock.Now += TimeSpan.FromSeconds(31);
        var served = await resolver.ResolveAsync("acme.taxoffix.io");
        await WaitForCallsAsync(auth, 2);
        await Task.Delay(20);
        var afterRevalidation = await resolver.ResolveAsync("acme.taxoffix.io");

        Assert.Equal(OldTenant, served.TenantId);
        Assert.Equal(NewTenant, afterRevalidation.TenantId);
    }

    [Fact]
    public async Task Un_host_deshabilitado_deja_de_resolver_tras_la_revalidacion()
    {
        var (resolver, auth, clock) = Build();
        auth.Next = () => TenantIs(OldTenant);
        await resolver.ResolveAsync("old-name.taxoffix.io");

        auth.Next = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        clock.Now += TimeSpan.FromSeconds(31);
        await resolver.ResolveAsync("old-name.taxoffix.io");
        await WaitForCallsAsync(auth, 2);
        await Task.Delay(20);

        var result = await resolver.ResolveAsync("old-name.taxoffix.io");
        Assert.Equal(HostTenantOutcome.NotRegistered, result.Outcome);
    }

    [Fact]
    public async Task Si_Auth_no_contesta_al_revalidar_se_conserva_la_cacheada()
    {
        var (resolver, auth, clock) = Build();
        auth.Next = () => TenantIs(OldTenant);
        await resolver.ResolveAsync("acme.taxoffix.io");

        auth.Next = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        clock.Now += TimeSpan.FromSeconds(31);
        await resolver.ResolveAsync("acme.taxoffix.io");
        await WaitForCallsAsync(auth, 2);
        await Task.Delay(20);

        var result = await resolver.ResolveAsync("acme.taxoffix.io");
        Assert.Equal(OldTenant, result.TenantId);
    }

    [Fact]
    public async Task Pasado_el_tope_vuelve_a_preguntar_antes_de_responder()
    {
        var (resolver, auth, clock) = Build();
        auth.Next = () => TenantIs(OldTenant);
        await resolver.ResolveAsync("acme.taxoffix.io");

        auth.Next = () => TenantIs(NewTenant);
        clock.Now += TimeSpan.FromMinutes(16);
        var result = await resolver.ResolveAsync("acme.taxoffix.io");

        Assert.Equal(NewTenant, result.TenantId);
    }

    [Fact]
    public async Task Un_fallo_de_Auth_no_se_cachea()
    {
        var (resolver, auth, _) = Build();
        auth.Next = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        Assert.Equal(HostTenantOutcome.Unavailable, (await resolver.ResolveAsync("acme.taxoffix.io")).Outcome);

        auth.Next = () => TenantIs(OldTenant);
        var result = await resolver.ResolveAsync("acme.taxoffix.io");

        Assert.Equal(OldTenant, result.TenantId);
        Assert.Equal(2, auth.Calls);
    }

    [Fact]
    public async Task La_cache_es_compartida_entre_instancias_del_resolver()
    {
        var cache = new HostTenantCache();
        var (first, firstAuth, _) = Build(cache);
        firstAuth.Next = () => TenantIs(OldTenant);
        await first.ResolveAsync("acme.taxoffix.io");

        var (second, secondAuth, _) = Build(cache);
        var result = await second.ResolveAsync("acme.taxoffix.io");

        Assert.Equal(OldTenant, result.TenantId);
        Assert.Equal(0, secondAuth.Calls);
    }
}
