using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.CustomerVisibility;
using Microsoft.Extensions.Logging;
using TaxVision.Sms.Application.Customers.Abstractions;

namespace TaxVision.Sms.Infrastructure.Customers;

/// <summary>Opciones de la reconciliación de la política de contacto (sección <c>Sms:ContactPolicy</c>).</summary>
public sealed class SmsContactPolicyOptions
{
    public const string SectionName = "Sms:ContactPolicy";

    /// <summary>Vacía = la misma base de Customer que usa la reconciliación de visibilidad.</summary>
    public string CustomerBaseUrl { get; set; } = string.Empty;

    public bool ReconciliationEnabled { get; set; } = true;

    public int ReconciliationIntervalHours { get; set; } = 6;

    public int ReconciliationPageSize { get; set; } = 200;
}

/// <summary>
/// Cliente M2M de solo lectura a <c>GET internal/customers/reconciliation</c> (cross-tenant, solo
/// PlatformTenant), el mismo que usa Notification. Nunca lanza: <c>null</c> ante cualquier fallo.
/// </summary>
internal sealed class SmsCustomerPolicyClient(
    HttpClient httpClient,
    IPlatformServiceTokenProvider tokenProvider,
    ILogger<SmsCustomerPolicyClient> logger
) : ISmsCustomerPolicyClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<RemoteCustomerContactPolicyPage?> ListPageAsync(
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var token = await tokenProvider.GetPlatformTokenAsync(ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Contact policy reconciliation aborted: no PlatformTenant service token.");
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
                    "Contact policy reconciliation listing failed ({Status}) on page {Page}.",
                    (int)response.StatusCode,
                    page
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<PageDto>(Json, ct);
            if (dto is null)
                return null;

            var items = dto
                .Items.Select(x => new RemoteCustomerContactPolicy(x.TenantId, x.CustomerId, x.SmsEnabled))
                .ToList();
            return new RemoteCustomerContactPolicyPage(items, dto.Page * dto.Size < dto.TotalCount);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Contact policy reconciliation could not read Customer on page {Page}.", page);
            return null;
        }
    }

    private sealed record PageDto(IReadOnlyList<ItemDto> Items, int Page, int Size, int TotalCount);

    private sealed record ItemDto(Guid TenantId, Guid CustomerId, bool? SmsEnabled = null);
}
