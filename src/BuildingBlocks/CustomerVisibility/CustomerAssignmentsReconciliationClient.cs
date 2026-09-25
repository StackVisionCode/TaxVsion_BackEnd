using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.CustomerVisibility;

/// <summary>Cliente M2M read-only hacia <c>GET internal/customers/assignments/reconciliation</c> (Customer.Api,
/// cross-tenant, solo PlatformTenant). Nunca lanza: null en cualquier fallo de token/HTTP.</summary>
public interface ICustomerAssignmentsReconciliationClient
{
    Task<CustomerAssignmentsReconPage?> ListPageAsync(int page, int size, CancellationToken ct = default);
}

public sealed record CustomerAssignmentsReconPage(IReadOnlyList<RemoteCustomerAssignments> Items, bool HasMore);

public sealed record RemoteCustomerAssignments(
    Guid TenantId,
    Guid CustomerId,
    IReadOnlyList<Guid> AssigneeUserIds,
    DateTime Version
);

internal sealed class CustomerAssignmentsReconciliationClient(
    HttpClient httpClient,
    IPlatformServiceTokenProvider tokenProvider,
    ILogger<CustomerAssignmentsReconciliationClient> logger
) : ICustomerAssignmentsReconciliationClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<CustomerAssignmentsReconPage?> ListPageAsync(int page, int size, CancellationToken ct = default)
    {
        var token = await tokenProvider.GetPlatformTokenAsync(ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Assignments reconciliation aborted: could not acquire PlatformTenant token.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/customers/assignments/reconciliation?page={page}&size={size}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Assignments reconciliation listing failed ({Status}) on page {Page}.",
                    (int)response.StatusCode,
                    page
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<PageDto>(Json, ct);
            if (dto is null)
                return null;

            var items = dto
                .Items.Select(i => new RemoteCustomerAssignments(
                    i.TenantId,
                    i.CustomerId,
                    i.AssigneeUserIds ?? [],
                    i.Version
                ))
                .ToList();
            return new CustomerAssignmentsReconPage(items, page * size < dto.TotalCount);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Assignments reconciliation errored on page {Page}.", page);
            return null;
        }
    }

    private sealed record PageDto(List<ItemDto> Items, int TotalCount);

    private sealed record ItemDto(Guid TenantId, Guid CustomerId, List<Guid>? AssigneeUserIds, DateTime Version);
}
