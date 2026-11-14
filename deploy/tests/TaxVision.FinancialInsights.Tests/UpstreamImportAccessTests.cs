using System.Net;
using Microsoft.Extensions.Configuration;
using TaxVision.FinancialInsights.Api.Imports;
using Xunit;

namespace TaxVision.FinancialInsights.Tests;

public sealed class UpstreamImportAccessTests
{
    [Fact]
    public async Task Requires_effective_permission_and_module()
    {
        var gateway = Gateway(path => path.StartsWith("/auth/")
            ? Json("""{"actorType":"TenantEmployee","enabledModules":["financial_insights"],"permissions":[{"code":"financial.import","effective":true}]}""")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal(ImportAccessResult.Granted, await gateway.CanImportAsync("token", CancellationToken.None));

        var withoutModule = Gateway(_ => Json("""{"actorType":"TenantEmployee","enabledModules":[],"permissions":[{"code":"financial.import","effective":true}]}"""));
        Assert.Equal(ImportAccessResult.ModuleUnavailable, await withoutModule.CanImportAsync("token", CancellationToken.None));

        var portal = Gateway(_ => Json("""{"actorType":"CustomerPortal","enabledModules":["financial_insights"],"permissions":[{"code":"financial.import","effective":true}]}"""));
        Assert.Equal(ImportAccessResult.PermissionDenied, await portal.CanImportAsync("token", CancellationToken.None));
    }

    [Fact]
    public async Task Portal_import_requires_portal_actor_upload_permission_and_financial_module()
    {
        var granted = Gateway(_ => Json("""{"actorType":"CustomerPortal","enabledModules":["financial_insights","documents"],"permissions":[{"code":"financial.portal.import","effective":true},{"code":"cloudstorage.file.upload","effective":true}]}"""));
        Assert.Equal(ImportAccessResult.Granted, await granted.CanPortalImportAsync("token", CancellationToken.None));

        var staff = Gateway(_ => Json("""{"actorType":"TenantEmployee","enabledModules":["financial_insights","documents"],"permissions":[{"code":"financial.portal.import","effective":true},{"code":"cloudstorage.file.upload","effective":true}]}"""));
        Assert.Equal(ImportAccessResult.PermissionDenied, await staff.CanPortalImportAsync("token", CancellationToken.None));

        var denied = Gateway(_ => Json("""{"actorType":"CustomerPortal","enabledModules":["financial_insights","documents"],"permissions":[{"code":"financial.portal.import","effective":false},{"code":"cloudstorage.file.upload","effective":true}]}"""));
        Assert.Equal(ImportAccessResult.PermissionDenied, await denied.CanPortalImportAsync("token", CancellationToken.None));

        var noModule = Gateway(_ => Json("""{"actorType":"CustomerPortal","enabledModules":[],"permissions":[{"code":"financial.portal.import","effective":true},{"code":"cloudstorage.file.upload","effective":true}]}"""));
        Assert.Equal(ImportAccessResult.ModuleUnavailable, await noModule.CanPortalImportAsync("token", CancellationToken.None));

        var noUpload = Gateway(_ => Json("""{"actorType":"CustomerPortal","enabledModules":["financial_insights","documents"],"permissions":[{"code":"financial.portal.import","effective":true},{"code":"cloudstorage.file.upload","effective":false}]}"""));
        Assert.Equal(ImportAccessResult.PermissionDenied, await noUpload.CanPortalImportAsync("token", CancellationToken.None));
    }

    [Fact]
    public async Task Requires_same_owner_and_completed_scan()
    {
        var customer = Guid.NewGuid(); var file = Guid.NewGuid();
        var other = Guid.NewGuid();
        var wrongOwner = Gateway(_ => Json($$"""{"id":"{{file}}","ownerType":"Customer","ownerId":"{{other}}","status":"Available","detectedContentType":"application/pdf","sizeBytes":42}"""));
        Assert.False(await wrongOwner.CanRegisterFileAsync(file, customer, "token", CancellationToken.None));

        var pending = Gateway(_ => Json($$"""{"id":"{{file}}","ownerType":"Customer","ownerId":"{{customer}}","status":"PendingScan","detectedContentType":"application/pdf","sizeBytes":42}"""));
        Assert.False(await pending.CanRegisterFileAsync(file, customer, "token", CancellationToken.None));

        var available = Gateway(_ => Json($$"""{"id":"{{file}}","ownerType":1,"ownerId":"{{customer}}","status":3,"detectedContentType":"application/pdf","sizeBytes":42}"""));
        Assert.True(await available.CanRegisterFileAsync(file, customer, "token", CancellationToken.None));
    }

    [Fact]
    public async Task Portal_registration_waits_for_scan_of_own_file()
    {
        var customer = Guid.NewGuid(); var file = Guid.NewGuid();
        var pending = Gateway(_ => Json($$"""{"id":"{{file}}","ownerType":"Customer","ownerId":"{{customer}}","status":"PendingScan","declaredContentType":"application/pdf","sizeBytes":42}"""));
        Assert.Equal(PortalFileEligibility.AwaitingScan, await pending.CanRegisterPortalFileAsync(file, customer, "token", CancellationToken.None));
        var otherOwner = Gateway(_ => Json($$"""{"id":"{{file}}","ownerType":"Customer","ownerId":"{{Guid.NewGuid()}}","status":"PendingScan","declaredContentType":"application/pdf","sizeBytes":42}"""));
        Assert.Equal(PortalFileEligibility.Ineligible, await otherOwner.CanRegisterPortalFileAsync(file, customer, "token", CancellationToken.None));
    }

    [Fact]
    public async Task Customer_lookup_rejects_other_tenant_and_inactive_status()
    {
        var tenant = Guid.NewGuid(); var customer = Guid.NewGuid();
        var other = Guid.NewGuid();
        var crossTenant = Gateway(_ => Json($$"""{"id":"{{customer}}","tenantId":"{{other}}","status":"Active","displayName":"Client"}"""));
        Assert.False((await crossTenant.CanAccessCustomerAsync(customer, tenant, "token", CancellationToken.None))!.IsAccessible);
        var inactive = Gateway(_ => Json($$"""{"id":"{{customer}}","tenantId":"{{tenant}}","status":"Inactive","displayName":"Client"}"""));
        Assert.False((await inactive.CanAccessCustomerAsync(customer, tenant, "token", CancellationToken.None))!.IsAccessible);
        var active = Gateway(_ => Json($$"""{"id":"{{customer}}","tenantId":"{{tenant}}","status":"Active","displayName":"Client"}"""));
        Assert.True((await active.CanAccessCustomerAsync(customer, tenant, "token", CancellationToken.None))!.IsAccessible);
    }

    private static UpstreamImportAccess Gateway(Func<string, HttpResponseMessage> respond)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Auth:BaseUrl"] = "https://auth.test/",
            ["Services:Customer:BaseUrl"] = "https://customer.test/",
            ["Services:CloudStorage:BaseUrl"] = "https://storage.test/"
        }).Build();
        return new UpstreamImportAccess(new Factory(new Handler(respond)), config);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    { Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json") };

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request.RequestUri!.AbsolutePath));
    }
}
