using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Contacts.Abstractions;

namespace TaxVision.Campaigns.Infrastructure.Customers;

public sealed class CustomerServiceOptions
{
    public const string SectionName = "CustomerService";

    /// <summary>Base URL del servicio Customer. En Docker: http://customer-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5263";
}

/// <summary>
/// Cliente M2M del directorio de Customer. Adquiere un token de servicio del tenant
/// (<see cref="IServiceTokenAcquirer"/>) y pagina <c>GET /internal/customers/list?status=Active</c>
/// (endpoint ServiceOnly, tenant tomado del token — NO expuesto en el Gateway público, se llama directo).
/// Fail-open: si Customer no responde o no hay token, devuelve lista vacía (la campaña simplemente no
/// suma clientes; no rompe el envío). SIN dinero.
/// </summary>
public sealed class CustomerAudienceClient(
    HttpClient http,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<CustomerAudienceClient> logger
) : ICustomerAudienceClient
{
    private const int PageSize = 200;
    private const int MaxPages = 100; // tope de seguridad (20k clientes)

    public async Task<IReadOnlyList<CustomerAudienceMember>> GetActiveCustomersAsync(
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("No service token for tenant {TenantId}; skipping Customer audience.", tenantId);
            return [];
        }

        var members = new List<CustomerAudienceMember>();
        try
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"internal/customers/list?status=Active&page={page}&size={PageSize}"
                );
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Customer list returned {StatusCode} for tenant {TenantId} (page {Page}); using what we have.",
                        (int)response.StatusCode,
                        tenantId,
                        page
                    );
                    break;
                }

                var body = await response.Content.ReadFromJsonAsync<CustomerListPage>(ct);
                if (body?.Items is null || body.Items.Count == 0)
                    break;

                members.AddRange(body.Items.Select(i => new CustomerAudienceMember(i.Id, i.PrimaryEmail, i.PrimaryPhone)));

                if (page >= body.TotalPagesSafe(PageSize))
                    break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not reach Customer for tenant {TenantId}; skipping Customer audience.", tenantId);
        }

        return members;
    }

    // Shape parcial de PagedResult<CustomerSummaryResponse> (solo lo que necesitamos).
    private sealed record CustomerListPage(List<CustomerSummaryDto> Items, int Page, int Size, int TotalCount)
    {
        public int TotalPagesSafe(int size) => size <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / size);
    }

    private sealed record CustomerSummaryDto(Guid Id, string DisplayName, string PrimaryEmail, string? PrimaryPhone);
}
