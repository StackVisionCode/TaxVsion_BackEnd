using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.Infrastructure.ClientForms.Security;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormPublicTenantHostValidatorTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Resolved_host_is_cached_so_auth_is_called_once()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, Resolution(TenantId)));
        var validator = Create(handler);

        Assert.True(await validator.MatchesTenantAsync(TenantId, "Office.Test"));
        Assert.True(await validator.MatchesTenantAsync(TenantId, "office.test"));
        Assert.False(await validator.MatchesTenantAsync(Guid.NewGuid(), "office.test"));
        Assert.Equal(1, handler.Calls);
        Assert.Equal("office.test", handler.LastHost);
    }

    [Fact]
    public async Task Unknown_host_is_cached_as_negative()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        var validator = Create(handler);

        Assert.False(await validator.MatchesTenantAsync(TenantId, "unknown.test"));
        Assert.False(await validator.MatchesTenantAsync(TenantId, "unknown.test"));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Auth_rejection_fails_closed_without_throwing(HttpStatusCode status)
    {
        var validator = Create(new ScriptedHandler(_ => Json(status, "{}")));

        Assert.False(await validator.MatchesTenantAsync(TenantId, "office.test"));
    }

    [Fact]
    public async Task Network_failure_fails_closed_without_throwing()
    {
        var validator = Create(new ScriptedHandler(_ => throw new HttpRequestException("auth down")));

        Assert.False(await validator.MatchesTenantAsync(TenantId, "office.test"));
    }

    [Fact]
    public async Task Malformed_response_fails_closed_without_throwing()
    {
        var validator = Create(new ScriptedHandler(_ => Json(HttpStatusCode.OK, "{\"tenantId\":\"not-a-guid\"")));

        Assert.False(await validator.MatchesTenantAsync(TenantId, "office.test"));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        var validator = Create(new ScriptedHandler(_ => Json(HttpStatusCode.OK, Resolution(TenantId))));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validator.MatchesTenantAsync(TenantId, "office.test", cancelled.Token)
        );
    }

    [Fact]
    public async Task Development_host_matches_any_office_without_calling_auth_in_development()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        var validator = Create(handler, Environments.Development, "localhost");

        Assert.True(await validator.MatchesTenantAsync(TenantId, "LocalHost"));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Development_hosts_are_ignored_outside_development()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        var validator = Create(handler, Environments.Production, "localhost");

        Assert.False(await validator.MatchesTenantAsync(TenantId, "localhost"));
        Assert.Equal(1, handler.Calls);
    }

    private static FormPublicTenantHostValidator Create(
        HttpMessageHandler handler,
        string environment = "Production",
        params string[] developmentHosts
    ) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://auth.test/") },
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<FormPublicTenantHostValidator>.Instance,
            Options.Create(
                new ClientFormsOptions
                {
                    External = new ClientFormsExternalOptions { DevelopmentHosts = developmentHosts },
                }
            ),
            new TestHostEnvironment(environment)
        );

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static string Resolution(Guid tenantId) =>
        $"{{\"tenantId\":\"{tenantId}\",\"name\":\"Office\",\"status\":\"Active\"}}";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastHost { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastHost = request.Headers.Host;
            return Task.FromResult(respond(request));
        }
    }
}
