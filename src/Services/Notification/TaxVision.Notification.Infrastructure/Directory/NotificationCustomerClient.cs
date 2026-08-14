using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Directory.Abstractions;

namespace TaxVision.Notification.Infrastructure.Directory;

public sealed class NotificationCustomerClientOptions
{
    public const string SectionName = "Notification:Customer";

    /// <summary>Local: http://localhost:5263. En Docker: http://customer-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5263";

    public bool ReconciliationEnabled { get; set; } = true;

    public int ReconciliationIntervalHours { get; set; } = 6;

    public int ReconciliationPageSize { get; set; } = 200;
}

/// <summary>
/// Cliente M2M de solo lectura hacia Customer. Reutiliza el <see cref="IServiceTokenAcquirer"/> que
/// el servicio ya tenía y sólo agrega un HttpClient tipado. Nunca lanza: ante cualquier fallo
/// devuelve <c>null</c> y el barrido se reintenta en la corrida siguiente.
/// </summary>
internal sealed class NotificationCustomerClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<NotificationCustomerClient> logger
) : INotificationCustomerClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<RemoteCustomerPage?> ListAllForReconciliationAsync(
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
            logger.LogWarning("Customer directory reconciliation aborted: no PlatformTenant service token.");
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
                    "Customer directory reconciliation listing failed ({Status}) on page {Page}.",
                    (int)response.StatusCode,
                    page
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<PageDto>(Json, ct);
            if (dto is null)
                return null;

            var items = dto
                .Items.Select(x => new RemoteCustomerContact(
                    x.TenantId,
                    x.CustomerId,
                    x.DisplayName,
                    x.PrimaryEmail,
                    string.Equals(x.Status, "Active", StringComparison.OrdinalIgnoreCase)
                ))
                .ToList();

            return new RemoteCustomerPage(items, dto.Page * dto.Size < dto.TotalCount);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Customer directory reconciliation could not reach Customer on page {Page}.", page);
            return null;
        }
    }

    private sealed record PageDto(IReadOnlyList<ItemDto> Items, int Page, int Size, int TotalCount);

    private sealed record ItemDto(
        Guid TenantId,
        Guid CustomerId,
        string DisplayName,
        string PrimaryEmail,
        string Status
    );
}
