using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Infrastructure.Reconciliation;

/// <summary>
/// Cliente M2M hacia <c>GET customers/internal/reconciliation</c> (cross-tenant, solo PlatformTenant).
/// Reusa el mismo <see cref="IServiceTokenAcquirer"/> que el resto de Correspondence
/// (<c>CorrespondenceServiceTokenAcquirer</c>, registrado por forwarding), pero pide el token para
/// <see cref="PlatformTenant"/> (única identidad autorizada por el gate del endpoint). Nunca lanza:
/// null en cualquier fallo de token/HTTP para que el job aborte esa corrida y reintente en la siguiente.
/// </summary>
internal sealed class CorrespondenceCustomerReconciliationClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<CorrespondenceCustomerReconciliationClient> logger
) : ICustomerReconciliationClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<CustomerReconciliationPage?> ListPageAsync(int page, int size, CancellationToken ct = default)
    {
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

            var dto = await response.Content.ReadFromJsonAsync<CustomerPageDto>(Json, ct);
            if (dto is null)
                return null;

            var items = dto
                .Items.Select(x => new RemoteCustomerRecord(
                    x.TenantId,
                    x.CustomerId,
                    x.DisplayName,
                    x.PrimaryEmail,
                    string.Equals(x.Status, "Active", StringComparison.OrdinalIgnoreCase)
                ))
                .ToList();

            var hasMore = dto.Page * dto.Size < dto.TotalCount;
            return new CustomerReconciliationPage(items, hasMore);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Customer reconciliation listing threw on page {Page}.", page);
            return null;
        }
    }

    private sealed record CustomerPageDto(IReadOnlyList<CustomerDto> Items, int Page, int Size, int TotalCount);

    private sealed record CustomerDto(
        Guid TenantId,
        Guid CustomerId,
        string DisplayName,
        string PrimaryEmail,
        string Status
    );
}
