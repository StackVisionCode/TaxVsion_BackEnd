using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Customers.Abstractions;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Infrastructure.Customers;

public sealed class CustomerClientOptions
{
    public const string SectionName = "Tasks:Customer";

    /// <summary>Base URL de Customer. Local: http://localhost:5263. En Docker: http://customer-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5263";

    /// <summary>
    /// Encendida por defecto: la reconciliación completa nunca borra, sólo inserta lo que falta y
    /// refresca nombre y status contra la fuente autoritativa.
    /// </summary>
    public bool ReconciliationEnabled { get; set; } = true;

    public int ReconciliationIntervalHours { get; set; } = 12;

    public int ReconciliationPageSize { get; set; } = 200;
}

/// <summary>
/// Cliente M2M de solo lectura hacia Customer. Reutiliza el <see cref="IServiceTokenAcquirer"/> del
/// servicio —que ya apunta a Auth— y sólo agrega un HttpClient tipado hacia otro destino. Nunca
/// lanza: devuelve <c>null</c> ante cualquier fallo de token o HTTP.
/// </summary>
internal sealed class TasksCustomerClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<TasksCustomerClient> logger
) : ITasksCustomerClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return null;

        var dto = await FetchPageAsync(token, page, size, ct);
        return dto is null
            ? null
            : new PagedResult<RemoteCustomerSummary>(
                dto.Items.Select(x => new RemoteCustomerSummary(x.Id, x.DisplayName, x.Status == "Active")).ToList(),
                dto.Page,
                dto.Size,
                dto.TotalCount
            );
    }

    public async Task<RemoteCustomerReconciliationPage?> ListAllForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        // El endpoint cross-tenant sólo acepta un token cuyo tenant sea el PlatformTenant: mismo
        // acquirer, distinta identidad pedida.
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Customer reconciliation aborted: could not acquire PlatformTenant service token.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/customers/reconciliation?status=All&page={page}&size={size}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Customer reconciliation listing failed ({Status}) on page {Page}.",
                    (int)response.StatusCode,
                    page
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<ReconciliationPageDto>(Json, ct);
            if (dto is null)
                return null;

            var items = dto
                .Items.Select(x => new RemoteReconciliationCustomer(
                    x.TenantId,
                    x.CustomerId,
                    x.DisplayName,
                    MapStatus(x.Status)
                ))
                .ToList();

            var hasMore = dto.Page * dto.Size < dto.TotalCount;
            return new RemoteCustomerReconciliationPage(items, hasMore);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Customer reconciliation listing threw on page {Page}.", page);
            return null;
        }
    }

    private static CustomerDirectoryStatus MapStatus(string status) =>
        Enum.TryParse<CustomerDirectoryStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : CustomerDirectoryStatus.Active;

    private async Task<CustomerPageDto?> FetchPageAsync(string token, int page, int size, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/customers/list?status=Active&page={page}&size={size}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Customer internal listing call failed ({Status}).", (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CustomerPageDto>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Customer internal listing call threw.");
            return null;
        }
    }

    private sealed record CustomerPageDto(IReadOnlyList<CustomerSummaryDto> Items, int Page, int Size, int TotalCount);

    private sealed record CustomerSummaryDto(Guid Id, string DisplayName, string Status);

    private sealed record ReconciliationPageDto(
        IReadOnlyList<ReconciliationCustomerDto> Items,
        int Page,
        int Size,
        int TotalCount
    );

    private sealed record ReconciliationCustomerDto(
        Guid TenantId,
        Guid CustomerId,
        string DisplayName,
        string PrimaryEmail,
        string Status
    );
}
