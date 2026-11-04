using System.Net;
using System.Text;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using TaxVision.Tenant.Infrastructure.Billing;
using TaxVision.Tenant.Infrastructure.Branding;

namespace TaxVision.Tenant.Tests.Infrastructure;

/// <summary>Contrato acordado con Billing para <c>GET internal/billing/issuer-profiles</c>.</summary>
public sealed class BillingIssuerProfileClientTests
{
    private const string ContractPage = """
        {
          "items": [
            {
              "tenantId": "0b6f4c1e-7d2a-4f7e-9a51-3c2d1e0f9a88",
              "name": "Acme Tax LLC",
              "taxId": "12-3456789",
              "line1": "100 Main St",
              "line2": null,
              "city": "Austin",
              "state": "TX",
              "zip": "78701",
              "country": "US",
              "phone": "(555) 123-4567",
              "email": "office@acmetax.com",
              "website": "https://acmetax.com",
              "updatedAtUtc": "2026-10-01T12:00:00Z"
            }
          ],
          "page": 2,
          "size": 50,
          "totalCount": 51
        }
        """;

    [Fact]
    public async Task Reads_the_agreed_page_shape_with_a_platform_service_token()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ContractPage);
        var tokens = new FakeTokenAcquirer("service-token");
        var client = CreateClient(handler, tokens);

        var result = await client.GetPageAsync(2, 50);

        Assert.True(result.IsSuccess);
        Assert.Equal(51, result.Value.TotalCount);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(Guid.Parse("0b6f4c1e-7d2a-4f7e-9a51-3c2d1e0f9a88"), item.TenantId);
        Assert.Equal("Acme Tax LLC", item.Name);
        Assert.Equal("78701", item.Zip);
        Assert.Equal("https://acmetax.com", item.Website);

        Assert.Equal(
            "http://billing.test/internal/billing/issuer-profiles?page=2&size=50",
            handler.LastUri?.ToString()
        );
        Assert.Equal("Bearer service-token", handler.LastAuthorization);
        Assert.Equal(PlatformTenant.Id, tokens.RequestedFor);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_non_success_status_is_unavailable_not_an_empty_page(HttpStatusCode status)
    {
        var client = CreateClient(new StubHandler(status, "{}"), new FakeTokenAcquirer("token"));

        var result = await client.GetPageAsync(1, 100);

        Assert.Equal(BillingIssuerProfileErrors.Unavailable, result.Error);
    }

    [Fact]
    public async Task Without_a_service_token_billing_is_not_called()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ContractPage);
        var client = CreateClient(handler, new FakeTokenAcquirer(null));

        var result = await client.GetPageAsync(1, 100);

        Assert.Equal(BillingIssuerProfileErrors.Unavailable, result.Error);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task An_unreadable_body_is_unavailable()
    {
        var client = CreateClient(new StubHandler(HttpStatusCode.OK, "<html>"), new FakeTokenAcquirer("token"));

        var result = await client.GetPageAsync(1, 100);

        Assert.Equal(BillingIssuerProfileErrors.Unavailable, result.Error);
    }

    private static BillingIssuerProfileClient CreateClient(StubHandler handler, FakeTokenAcquirer tokens) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://billing.test/") },
            tokens,
            NullLogger<BillingIssuerProfileClient>.Instance
        );

    private sealed class FakeTokenAcquirer(string? token) : ITenantServiceTokenAcquirer
    {
        public Guid? RequestedFor { get; private set; }

        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
        {
            RequestedFor = tenantId;
            return Task.FromResult(token);
        }
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
        }
    }
}
