using System.Net;
using System.Security.Claims;
using System.Text;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Infrastructure.Upstream;

namespace TaxVision.FinancialInsights.Tests.Infrastructure;

public sealed class UpstreamClientTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task File_gateway_requires_same_owner_and_completed_scan()
    {
        var customer = Guid.NewGuid();
        var file = Guid.NewGuid();
        var other = Guid.NewGuid();

        Assert.Equal(
            ImportFileState.Ineligible,
            await Files(_ => Json($$"""{"id":"{{file}}","ownerType":"Customer","ownerId":"{{other}}","status":"Available","detectedContentType":"application/pdf","sizeBytes":42}"""))
                .CheckAsync(file, customer)
        );
        Assert.Equal(
            ImportFileState.Ready,
            await Files(_ => Json($$"""{"id":"{{file}}","ownerType":1,"ownerId":"{{customer}}","status":3,"detectedContentType":"application/pdf","sizeBytes":42}"""))
                .CheckAsync(file, customer)
        );
        Assert.Equal(
            ImportFileState.AwaitingScan,
            await Files(_ => Json($$"""{"id":"{{file}}","ownerType":"Customer","ownerId":"{{customer}}","status":"PendingScan","declaredContentType":"application/pdf","sizeBytes":42}"""))
                .CheckAsync(file, customer)
        );
        Assert.Equal(
            ImportFileState.Ineligible,
            await Files(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).CheckAsync(file, customer)
        );
        Assert.Equal(
            ImportFileState.Unavailable,
            await Files(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)).CheckAsync(file, customer)
        );
    }

    [Fact]
    public async Task File_gateway_forwards_the_callers_token_and_nothing_without_it()
    {
        string? sent = null;
        var gateway = Files(request =>
        {
            sent = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        await gateway.CheckAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal("user-token", sent);

        var anonymous = new CloudStorageImportFileGateway(
            new HttpClient(new Handler(_ => throw new InvalidOperationException("no call")))
            {
                BaseAddress = new Uri("https://storage.test/"),
            },
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() }
        );
        Assert.Equal(ImportFileState.Unavailable, await anonymous.CheckAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Entitlements_pull_reads_modules_and_quota_from_the_internal_endpoint_with_a_service_token()
    {
        var calls = new List<(string Path, string? Token)>();
        var client = Entitlements(
            "service-token",
            request =>
            {
                calls.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter));
                return Json(
                    """
                    {"tenantId":"x","revisionNumber":9,"planCode":"enterprise","entries":{
                      "module.financial_insights":{"valueType":"Bool","value":"True","status":"Active","source":"Plan"},
                      "module.documents":{"valueType":"Bool","value":"False","status":"Active","source":"Plan"},
                      "financial.documents_per_month":{"valueType":"Int","value":"200","status":"Active","source":"Plan"}}}
                    """
                );
            }
        );

        var snapshot = await client.FetchAsync(Tenant);

        Assert.Equal([($"/internal/tenants/{Tenant:D}/entitlements", (string?)"service-token")], calls);
        Assert.Equal(9, snapshot!.RevisionNumber);
        Assert.Equal("enterprise", snapshot.PlanCode);
        Assert.Equal(["financial_insights"], snapshot.EnabledModules);
        Assert.Equal(200, snapshot.DocumentsPerMonth);
    }

    [Fact]
    public async Task Entitlements_pull_returns_null_without_token_or_when_subscription_fails()
    {
        var calls = 0;
        Assert.Null(await Entitlements(null, _ => { calls++; return Json("{}"); }).FetchAsync(Tenant));
        Assert.Equal(0, calls);
        Assert.Null(await Entitlements("t", _ => new HttpResponseMessage(HttpStatusCode.NotFound)).FetchAsync(Tenant));
        Assert.Null(await Entitlements("t", _ => throw new HttpRequestException("down")).FetchAsync(Tenant));
    }

    private static CloudStorageImportFileGateway Files(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(
            new HttpClient(new Handler(respond)) { BaseAddress = new Uri("https://storage.test/") },
            Accessor("TenantEmployee", Tenant)
        );

    private static SubscriptionEntitlementsSnapshotClient Entitlements(
        string? token,
        Func<HttpRequestMessage, HttpResponseMessage> respond
    ) =>
        new(
            new HttpClient(new Handler(respond)) { BaseAddress = new Uri("https://subscription.test/") },
            new StubTokens(token)
        );

    private sealed class StubTokens(string? token) : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    private static HttpContextAccessor Accessor(string actor, Guid tenant)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimNames.ActorType, actor), new Claim(ClaimNames.TenantId, tenant.ToString())],
                    "test"
                )
            ),
        };
        context.Request.Headers.Authorization = "Bearer user-token";
        return new HttpContextAccessor { HttpContext = context };
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(respond(request));
    }
}
