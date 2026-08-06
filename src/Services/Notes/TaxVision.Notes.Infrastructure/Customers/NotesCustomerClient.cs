using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Customers.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Infrastructure.Customers;

public sealed class CustomerClientOptions
{
    public const string SectionName = "Notes:Customer";

    /// <summary>Base URL de Customer.Api. Local: http://localhost:5263. En Docker: http://customer-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5263";

    /// <summary>
    /// Habilitado por default: la reconciliación completa nunca borra datos, solo inserta filas
    /// faltantes y refresca nombre/status para converger con la fuente autoritativa (Customer) —
    /// mismo perfil de riesgo que el backfill reactivo, que tampoco tiene flag.
    /// </summary>
    public bool ReconciliationEnabled { get; set; } = true;

    /// <summary>Cada cuántas horas corre el barrido completo cross-tenant.</summary>
    public int ReconciliationIntervalHours { get; set; } = 12;

    /// <summary>Tamaño de página al paginar <c>customers/internal/reconciliation</c>.</summary>
    public int ReconciliationPageSize { get; set; } = 200;
}

// ---------------------------------------------------------------------------
// Fase 4B — cliente M2M read-only hacia GET customers/internal/list (policy ServiceOnly, ver
// contrato real leído directamente del código de Customer). Reutiliza el MISMO
// IServiceTokenAcquirer de Fase 4 (apunta a Auth) — Notes no necesita un segundo acquirer, solo un
// tercer HttpClient tipado hacia un downstream distinto (Customer, no Subscription). Mismo
// criterio confirmado por Correspondence: un acquirer por servicio, N HttpClients por destino real.
// Nunca lanza — null en cualquier falla de token/HTTP, el caller decide cómo reintentar/loguear.
// ---------------------------------------------------------------------------

internal sealed class NotesCustomerClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<NotesCustomerClient> logger
) : INotesCustomerClient
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
        // Cross-tenant: el endpoint solo acepta un token cuyo tenant_id == PlatformTenant.Id (no el
        // token per-tenant). Reusa el MISMO IServiceTokenAcquirer, solo cambia la identidad pedida.
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
                $"customers/internal/reconciliation?status=All&page={page}&size={size}"
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
                $"customers/internal/list?status=Active&page={page}&size={size}"
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
