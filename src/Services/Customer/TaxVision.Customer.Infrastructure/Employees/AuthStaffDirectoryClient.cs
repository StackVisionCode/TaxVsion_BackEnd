using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Infrastructure.Employees;

/// <summary>
/// Pide a Auth el staff de todos los tenants (<c>internal/users/staff/reconciliation</c>). El endpoint solo
/// acepta un token de servicio de PlatformTenant. Reutiliza el <see cref="IServiceTokenAcquirer"/> que ya
/// apunta a Auth. Nunca lanza.
/// </summary>
public sealed class AuthStaffDirectoryClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<AuthStaffDirectoryClient> logger
) : IAuthStaffDirectoryClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<RemoteStaffPage?> ListStaffAsync(int page, int size, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Employee directory reconciliation aborted: no PlatformTenant service token.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/users/staff/reconciliation?page={page}&size={size}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Employee directory reconciliation failed ({Status}) on page {Page}.",
                    (int)response.StatusCode,
                    page
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<PageDto>(Json, ct);
            return dto is null ? null : new RemoteStaffPage(dto.Items, dto.Page * dto.Size < dto.TotalCount);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Employee directory reconciliation could not read Auth on page {Page}.", page);
            return null;
        }
    }

    private sealed record PageDto(IReadOnlyList<RemoteStaffUser> Items, int Page, int Size, int TotalCount);
}
