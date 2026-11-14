using System.Net.Http.Headers;
using System.Text.Json;
using BuildingBlocks.Authorization;
using TaxVision.FinancialInsights.Application.Imports;

namespace TaxVision.FinancialInsights.Api.Imports;

public enum ImportAccessResult { Granted, PermissionDenied, ModuleUnavailable, Unavailable }
public enum PortalFileEligibility { Unavailable, Ineligible, AwaitingScan, Ready }
public sealed record CustomerAccessSnapshot(bool IsAccessible, string? DisplayName);

public sealed class UpstreamImportAccess(IHttpClientFactory clients, IConfiguration configuration)
{
    public Task<ImportAccessResult> CanImportAsync(string bearer, CancellationToken ct) =>
        CanAccessAsync(bearer, FinancialInsightsPermissions.Import, ct);

    public Task<ImportAccessResult> CanViewAsync(string bearer, CancellationToken ct) =>
        CanAccessAsync(bearer, FinancialInsightsPermissions.View, ct);

    public Task<ImportAccessResult> CanPortalImportAsync(string bearer, CancellationToken ct) =>
        CanAccessAsync(bearer, FinancialInsightsPermissions.PortalImport, ct, "CustomerPortal",
            CloudStoragePermissions.FileUpload, "documents");

    private async Task<ImportAccessResult> CanAccessAsync(string bearer, string requiredPermission, CancellationToken ct,
        string? requiredActor = null, string? additionalPermission = null, string? additionalModule = null)
    {
        using var response = await GetAsync("Auth", "auth/me/effective-access", bearer, ct);
        if (response is null || !response.IsSuccessStatusCode) return ImportAccessResult.Unavailable;
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!json.RootElement.TryGetProperty("actorType", out var actorType)
            || (requiredActor is null
                ? actorType.GetString() is not ("TenantEmployee" or "TenantAdmin" or "PlatformAdmin")
                : actorType.GetString() != requiredActor)
            || !json.RootElement.TryGetProperty("permissions", out var permissions))
            return ImportAccessResult.PermissionDenied;
        var granted = permissions.EnumerateArray().Any(x =>
            x.TryGetProperty("code", out var code)
            && code.GetString() == requiredPermission);
        if (!granted) return ImportAccessResult.PermissionDenied;
        if (!json.RootElement.TryGetProperty("enabledModules", out var modules)
            || !modules.EnumerateArray().Any(x => string.Equals(x.GetString(), "financial_insights", StringComparison.OrdinalIgnoreCase)))
            return ImportAccessResult.ModuleUnavailable;
        if (additionalModule is not null && !modules.EnumerateArray().Any(x =>
                string.Equals(x.GetString(), additionalModule, StringComparison.OrdinalIgnoreCase)))
            return ImportAccessResult.ModuleUnavailable;
        if (!permissions.EnumerateArray().Any(x =>
            x.TryGetProperty("code", out var code) && code.GetString() == requiredPermission
            && x.TryGetProperty("effective", out var effective) && effective.ValueKind == JsonValueKind.True))
            return ImportAccessResult.PermissionDenied;
        if (additionalPermission is not null && !permissions.EnumerateArray().Any(x =>
                x.TryGetProperty("code", out var code) && code.GetString() == additionalPermission
                && x.TryGetProperty("effective", out var effective) && effective.ValueKind == JsonValueKind.True))
            return ImportAccessResult.PermissionDenied;
        return ImportAccessResult.Granted;
    }

    public async Task<CustomerAccessSnapshot?> CanAccessCustomerAsync(Guid customerId, Guid tenantId,
        string bearer, CancellationToken ct)
    {
        using var response = await GetAsync("Customer", $"customers/{customerId:D}", bearer, ct);
        if (response?.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
            return new CustomerAccessSnapshot(false, null);
        if (response is null || !response.IsSuccessStatusCode) return null;
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var customer = json.RootElement;
        var matches = customer.TryGetProperty("id", out var id) && id.GetGuid() == customerId
            && customer.TryGetProperty("tenantId", out var owner) && owner.GetGuid() == tenantId;
        var status = ReadEnum(customer, "status", "Active", "Inactive", "Archived");
        var name = customer.TryGetProperty("displayName", out var displayName) && displayName.ValueKind == JsonValueKind.String
            ? displayName.GetString() : null;
        return new CustomerAccessSnapshot(matches && status == "Active", name);
    }

    public async Task<bool?> CanRegisterFileAsync(Guid fileId, Guid customerId, string bearer, CancellationToken ct)
    {
        using var response = await GetAsync("CloudStorage", $"storage/files/{fileId:D}", bearer, ct);
        if (response?.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound) return false;
        if (response is null || !response.IsSuccessStatusCode) return null;
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var file = json.RootElement;
        if (!file.TryGetProperty("id", out var id) || id.GetGuid() != fileId) return false;
        var ownerType = ReadEnum(file, "ownerType", "Tenant", "Customer", "User", "Signature", "Invoice", "Communication");
        var status = ReadEnum(file, "status", "PendingUpload", "PendingScan", "Scanning", "Available", "Infected", "ScanFailed", "SoftDeleted", "BlockedByPolicy", "PendingReview");
        var ownerId = file.TryGetProperty("ownerId", out var owner) && owner.ValueKind == JsonValueKind.String ? owner.GetGuid() : (Guid?)null;
        var contentType = file.TryGetProperty("detectedContentType", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : null;
        var size = file.TryGetProperty("sizeBytes", out var bytes) && bytes.TryGetInt64(out var value) ? value : 0;
        return ImportFilePolicy.CanRegister(ownerType, ownerId, customerId, status, contentType, size);
    }

    public async Task<PortalFileEligibility> CanRegisterPortalFileAsync(Guid fileId, Guid customerId, string bearer, CancellationToken ct)
    {
        using var response = await GetAsync("CloudStorage", $"storage/files/{fileId:D}", bearer, ct);
        if (response?.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
            return PortalFileEligibility.Ineligible;
        if (response is null || !response.IsSuccessStatusCode) return PortalFileEligibility.Unavailable;
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var file = json.RootElement;
        if (!file.TryGetProperty("id", out var id) || id.GetGuid() != fileId) return PortalFileEligibility.Ineligible;
        var ownerType = ReadEnum(file, "ownerType", "Tenant", "Customer", "User", "Signature", "Invoice", "Communication");
        var status = ReadEnum(file, "status", "PendingUpload", "PendingScan", "Scanning", "Available", "Infected", "ScanFailed", "SoftDeleted", "BlockedByPolicy", "PendingReview");
        var ownerId = file.TryGetProperty("ownerId", out var owner) && owner.ValueKind == JsonValueKind.String ? owner.GetGuid() : (Guid?)null;
        var detected = file.TryGetProperty("detectedContentType", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : null;
        var declared = file.TryGetProperty("declaredContentType", out var declaredContent) && declaredContent.ValueKind == JsonValueKind.String ? declaredContent.GetString() : null;
        var size = file.TryGetProperty("sizeBytes", out var bytes) && bytes.TryGetInt64(out var value) ? value : 0;
        if (ImportFilePolicy.CanRegister(ownerType, ownerId, customerId, status, detected, size)) return PortalFileEligibility.Ready;
        if (ImportFilePolicy.CanAwaitScan(ownerType, ownerId, customerId, status, declared, size)) return PortalFileEligibility.AwaitingScan;
        return PortalFileEligibility.Ineligible;
    }

    private async Task<HttpResponseMessage?> GetAsync(string service, string path, string bearer, CancellationToken ct)
    {
        var baseUrl = configuration[$"Services:{service}:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https")) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        try
        {
            return await clients.CreateClient("financial-upstream").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    private static string? ReadEnum(JsonElement parent, string name, params string[] values)
    {
        if (!parent.TryGetProperty(name, out var element)) return null;
        if (element.ValueKind == JsonValueKind.String) return element.GetString();
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var index) && index >= 0 && index < values.Length)
            return values[index];
        return null;
    }
}
