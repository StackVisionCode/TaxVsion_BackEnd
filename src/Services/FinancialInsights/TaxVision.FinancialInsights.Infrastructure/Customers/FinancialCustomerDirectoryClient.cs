using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;

namespace TaxVision.FinancialInsights.Infrastructure.Customers;

public sealed record RemoteCustomer(Guid TenantId, Guid CustomerId, string? DisplayName, bool IsActive);

public sealed record RemoteCustomerPage(IReadOnlyList<RemoteCustomer> Items, bool HasMore);

/// <summary>Fuente autoritativa del directorio de clientes para las reconciliaciones (M2M a Customer.Api).</summary>
public interface IFinancialCustomerDirectoryClient
{
    /// <summary>Todos los tenants (token de la PlatformTenant). Null ante cualquier fallo.</summary>
    Task<RemoteCustomerPage?> ListAllAsync(int page, int size, CancellationToken ct = default);

    /// <summary>Clientes activos de un tenant (token de ese tenant). Null ante cualquier fallo.</summary>
    Task<RemoteCustomerPage?> ListActiveAsync(Guid tenantId, int page, int size, CancellationToken ct = default);
}

internal sealed class FinancialCustomerDirectoryClient(
    HttpClient http,
    IServiceTokenAcquirer tokens,
    ILogger<FinancialCustomerDirectoryClient> logger
) : IFinancialCustomerDirectoryClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<RemoteCustomerPage?> ListAllAsync(int page, int size, CancellationToken ct = default)
    {
        var dto = await GetAsync<ReconciliationPage>(
            PlatformTenant.Id,
            $"internal/customers/reconciliation?status=All&page={page}&size={size}",
            ct
        );
        return dto is null
            ? null
            : new RemoteCustomerPage(
                dto.Items.Select(i => new RemoteCustomer(i.TenantId, i.CustomerId, i.DisplayName, IsActive(i.Status))).ToArray(),
                dto.Page * dto.Size < dto.TotalCount
            );
    }

    public async Task<RemoteCustomerPage?> ListActiveAsync(Guid tenantId, int page, int size, CancellationToken ct = default)
    {
        var dto = await GetAsync<SummaryPage>(tenantId, $"internal/customers/list?status=Active&page={page}&size={size}", ct);
        return dto is null
            ? null
            : new RemoteCustomerPage(
                dto.Items.Select(i => new RemoteCustomer(tenantId, i.Id, i.DisplayName, IsActive(i.Status))).ToArray(),
                dto.Page * dto.Size < dto.TotalCount
            );
    }

    private static bool IsActive(string? status) => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    private async Task<T?> GetAsync<T>(Guid tokenTenant, string path, CancellationToken ct)
        where T : class
    {
        var token = await tokens.GetTokenAsync(tokenTenant, ct);
        if (string.IsNullOrEmpty(token) || http.BaseAddress is null)
        {
            logger.LogWarning("Customer directory call skipped: no service token or base URL.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Customer directory call failed with status {Status}.", (int)response.StatusCode);
                return null;
            }
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Customer directory call failed with {ExceptionType}.", ex.GetType().Name);
            return null;
        }
    }

    private sealed record ReconciliationPage(IReadOnlyList<ReconciliationItem> Items, int Page, int Size, int TotalCount);

    private sealed record ReconciliationItem(Guid TenantId, Guid CustomerId, string? DisplayName, string? Status);

    private sealed record SummaryPage(IReadOnlyList<SummaryItem> Items, int Page, int Size, int TotalCount);

    private sealed record SummaryItem(Guid Id, string? DisplayName, string? Status);
}
