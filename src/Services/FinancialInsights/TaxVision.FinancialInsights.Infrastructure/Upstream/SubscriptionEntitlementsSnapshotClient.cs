using System.Net.Http.Headers;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;

namespace TaxVision.FinancialInsights.Infrastructure.Upstream;

/// <summary>
/// Reparación por pull del plan: <c>GET internal/tenants/{id}/entitlements</c> de Subscription con token M2M
/// (actor Service). Sirve igual en requests de staff o portal, en el worker y en la reconciliación. Null ante
/// cualquier fallo; el caller decide.
/// </summary>
public sealed class SubscriptionEntitlementsSnapshotClient(HttpClient http, IServiceTokenAcquirer tokens)
    : ITenantEntitlementsSnapshotClient
{
    public async Task<TenantEntitlementsSnapshot?> FetchAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (http.BaseAddress is null)
            return null;
        var token = await tokens.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/tenants/{tenantId:D}/entitlements");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = json.RootElement;
            if (
                !root.TryGetProperty("revisionNumber", out var revision)
                || !revision.TryGetInt64(out var revisionNumber)
                || !root.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Object
            )
                return null;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
                    values[entry.Name] = value.GetString()!;
            }

            var planCode = root.TryGetProperty("planCode", out var plan) && plan.ValueKind == JsonValueKind.String
                ? plan.GetString()!
                : string.Empty;
            return new TenantEntitlementsSnapshot(
                planCode,
                revisionNumber,
                TenantEntitlementModuleExtensions.ExtractEnabledModules(values),
                FinancialEntitlements.ParseDocumentsPerMonth(values)
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }
}
