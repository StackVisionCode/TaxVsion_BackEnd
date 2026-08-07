using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TaxVision.Gateway.Middleware;
using Xunit;

namespace TaxVision.Gateway.Tests.Middleware;

/// <summary>GW-13 — los headers de seguridad no tenían cobertura.</summary>
public sealed class SecurityHeadersMiddlewareTests
{
    private static async Task<IHeaderDictionary> InvokeAsync(string environmentName)
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, new StubEnvironment(environmentName));

        await middleware.InvokeAsync(context);
        return context.Response.Headers;
    }

    // Literales y no Environments.*: son static readonly, no const, y [InlineData] exige constante.
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Los_tres_headers_base_van_en_todo_entorno(string environmentName)
    {
        var headers = await InvokeAsync(environmentName);

        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", headers["X-Frame-Options"]);
        Assert.Equal("no-referrer", headers["Referrer-Policy"]);
    }

    [Fact]
    public async Task HSTS_solo_fuera_de_desarrollo()
    {
        var production = await InvokeAsync(Environments.Production);
        var development = await InvokeAsync(Environments.Development);

        Assert.Equal("max-age=63072000; includeSubDomains", production["Strict-Transport-Security"]);
        Assert.False(development.ContainsKey("Strict-Transport-Security"));
    }

    private sealed class StubEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "TaxVision.Gateway.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
