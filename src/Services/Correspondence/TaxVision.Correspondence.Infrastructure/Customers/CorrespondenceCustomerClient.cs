using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Infrastructure.Customers;

/// <summary>
/// Implementación de <see cref="ICorrespondenceCustomerClient"/> contra
/// <c>GET /customers/internal/list</c> de Customer.Api (policy ServiceOnly).
/// </summary>
internal sealed class CorrespondenceCustomerClient(
    HttpClient httpClient,
    ICorrespondenceServiceTokenAcquirer tokenAcquirer,
    ILogger<CorrespondenceCustomerClient> logger
) : ICorrespondenceCustomerClient
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

        var page1 = await FetchPageAsync(token, page, size, ct);
        return page1 is null
            ? null
            : new PagedResult<RemoteCustomerSummary>(
                page1.Items.Select(x => new RemoteCustomerSummary(x.Id, x.PrimaryEmail, x.Status == "Active")).ToList(),
                page1.Page,
                page1.Size,
                page1.TotalCount
            );
    }

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
        // Fase 1 (hardening) — el filtro previo (`ex is not OperationCanceledException`) dejaba
        // escapar sin atrapar justo el TaskCanceledException que dispara el timeout de 30s del
        // HttpClient (ver DependencyInjection), violando el contrato documentado en
        // ICorrespondenceCustomerClient ("nunca lanza"). Mismo criterio que CloudStorageClient
        // (este mismo servicio) y CloudStorageOutboundAttachmentFetcher (Postmaster, Fase 13):
        // TaskCanceledException cubre tanto cancelación explícita como el timeout, y ambas se
        // traducen acá al mismo null que un status code no-success.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Customer internal listing call threw.");
            return null;
        }
    }

    private sealed record CustomerPageDto(IReadOnlyList<CustomerSummaryDto> Items, int Page, int Size, int TotalCount);

    private sealed record CustomerSummaryDto(Guid Id, string PrimaryEmail, string Status);
}
