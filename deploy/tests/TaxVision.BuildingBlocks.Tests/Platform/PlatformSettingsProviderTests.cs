using System.Net;
using System.Text;
using BuildingBlocks.Messaging.PlatformIntegrationEvents;
using BuildingBlocks.Platform;
using BuildingBlocks.Web.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Platform;

public sealed class PlatformSettingsProviderTests
{
    private static PlatformSettingsProvider NewProvider(PlatformSettingsOptions? options = null) =>
        new(Options.Create(options ?? new PlatformSettingsOptions()));

    private static PlatformSettingsSnapshot Snapshot(
        long version,
        string brand = "TaxOffix",
        string domain = "taxoffix.io"
    ) => new(version, brand, "TaxOffix LLC", domain, $"api.{domain}", $"support@{domain}", $"billing@{domain}");

    [Fact]
    public void Starts_with_the_local_config_as_version_zero()
    {
        var provider = NewProvider(new PlatformSettingsOptions { BrandName = "LocalBrand", BaseDomain = "local.test" });

        Assert.Equal(0, provider.Current.Version);
        Assert.Equal("LocalBrand", provider.Current.BrandName);
        Assert.Equal("app.local.test", provider.Current.AppHost);
        Assert.Equal("oficina1.local.test", provider.Current.TenantHost("oficina1"));
    }

    [Fact]
    public void A_newer_version_replaces_the_current_one()
    {
        var provider = NewProvider();

        Assert.True(provider.TryApply(Snapshot(3, brand: "TaxOffix Pro")));

        Assert.Equal(3, provider.Current.Version);
        Assert.Equal("TaxOffix Pro", provider.Current.BrandName);
    }

    [Fact]
    public void An_older_or_equal_version_never_overwrites_a_newer_one()
    {
        var provider = NewProvider();
        provider.TryApply(Snapshot(5, brand: "Newest"));

        Assert.False(provider.TryApply(Snapshot(4, brand: "Older")));
        Assert.False(provider.TryApply(Snapshot(5, brand: "Same version")));

        Assert.Equal("Newest", provider.Current.BrandName);
    }

    [Fact]
    public void The_event_handler_applies_the_snapshot_it_carries()
    {
        var provider = NewProvider();

        PlatformSettingsChangedHandler.Handle(
            new PlatformSettingsChangedIntegrationEvent
            {
                Version = 2,
                BrandName = "TaxOffix",
                LegalName = "TaxOffix LLC",
                BaseDomain = "taxoffix.com",
                ApiHost = "api.taxoffix.com",
                SupportEmail = "support@taxoffix.com",
                BillingEmail = "billing@taxoffix.com",
                ChangedAtUtc = DateTime.UtcNow,
            },
            provider,
            NullLogger<PlatformSettingsProvider>.Instance
        );

        Assert.Equal(2, provider.Current.Version);
        Assert.Equal("client.taxoffix.com", provider.Current.ClientHost);
    }

    [Fact]
    public async Task The_refresh_reads_tenant_and_applies_the_version()
    {
        var provider = NewProvider();
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """
            {"version":7,"brandName":"TaxOffix","legalName":"TaxOffix LLC","baseDomain":"taxoffix.io",
             "apiHost":"api.taxoffix.io","appHost":"app.taxoffix.io","clientHost":"client.taxoffix.io",
             "website":"https://taxoffix.io","supportEmail":"support@taxoffix.io",
             "billingEmail":"billing@taxoffix.io","updatedAtUtc":"2026-10-13T12:00:00Z"}
            """
        );
        var service = NewRefresh(provider, handler);

        var answered = await service.RefreshOnceAsync(CancellationToken.None);

        Assert.True(answered);
        Assert.Equal(7, provider.Current.Version);
        Assert.Equal("http://tenant-api:8080/platform/settings/public", handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task A_failing_refresh_keeps_the_current_version()
    {
        var provider = NewProvider();
        provider.TryApply(Snapshot(3));
        var service = NewRefresh(provider, new StubHandler(HttpStatusCode.ServiceUnavailable, "{}"));

        var answered = await service.RefreshOnceAsync(CancellationToken.None);

        Assert.False(answered);
        Assert.Equal(3, provider.Current.Version);
    }

    private static PlatformSettingsRefreshService NewRefresh(PlatformSettingsProvider provider, StubHandler handler) =>
        new(
            new StubHttpClientFactory(handler),
            provider,
            Options.Create(new PlatformSettingsOptions { TenantBaseUrl = "http://tenant-api:8080/" }),
            NullLogger<PlatformSettingsRefreshService>.Instance
        );

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastRequest = request;
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
