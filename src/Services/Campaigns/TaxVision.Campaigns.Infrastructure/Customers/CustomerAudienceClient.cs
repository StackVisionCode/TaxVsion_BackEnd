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
/// Fail-closed: si Customer no responde, no hay token o se llega al tope de páginas, el resultado sale
/// marcado como incompleto y el envío no usa la lista parcial (CMP-06). SIN dinero.
/// </summary>
public sealed class CustomerAudienceClient(
    HttpClient http,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<CustomerAudienceClient> logger
) : ICustomerAudienceClient
{
    private const int PageSize = 200;
    private const int MaxPages = 100; // tope de seguridad (20k clientes)

    public async Task<CustomerAudienceResult> GetActiveCustomersAsync(Guid tenantId, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("No service token for tenant {TenantId}; Customer audience unavailable.", tenantId);
            return new CustomerAudienceResult([], false, "no_service_token");
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
                        "Customer list returned {StatusCode} for tenant {TenantId} (page {Page}); audience incomplete.",
                        (int)response.StatusCode,
                        tenantId,
                        page
                    );
                    return new CustomerAudienceResult(members, false, $"http_{(int)response.StatusCode}");
                }

                var body = await response.Content.ReadFromJsonAsync<CustomerListPage>(ct);
                if (body?.Items is null || body.Items.Count == 0)
                    return new CustomerAudienceResult(members, true);

                members.AddRange(
                    body.Items.Select(i => new CustomerAudienceMember(
                        i.Id,
                        i.PrimaryEmail,
                        i.PrimaryPhone,
                        i.DisplayName
                    ))
                );

                if (page >= body.TotalPagesSafe(PageSize))
                    return new CustomerAudienceResult(members, true);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not reach Customer for tenant {TenantId}; audience incomplete.", tenantId);
            return new CustomerAudienceResult(members, false, "unreachable");
        }

        // Tope de páginas con páginas pendientes: antes truncaba en silencio.
        logger.LogWarning(
            "Customer list for tenant {TenantId} exceeds {MaxPages} pages; audience incomplete.",
            tenantId,
            MaxPages
        );
        return new CustomerAudienceResult(members, false, "max_pages");
    }

    // Shape parcial de PagedResult<CustomerSummaryResponse> (solo lo que necesitamos).
    private sealed record CustomerListPage(List<CustomerSummaryDto> Items, int Page, int Size, int TotalCount)
    {
        public int TotalPagesSafe(int size) => size <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / size);
    }

    private sealed record CustomerSummaryDto(Guid Id, string DisplayName, string PrimaryEmail, string? PrimaryPhone);
}
