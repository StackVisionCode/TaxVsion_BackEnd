using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Infrastructure.Notifications;

public sealed class NotificationClientOptions
{
    public const string SectionName = "Auth:Notification";

    public string BaseUrl { get; set; } = "http://localhost:5320";
}

/// <summary>
/// AJ-08: lee el estado del correo de un evento en Notification (M2M, token de servicio del tenant). Cualquier fallo
/// (red, timeout, 5xx, 401/403) devuelve Unavailable: el panel muestra "Status unavailable" y el GET no se rompe.
/// </summary>
public sealed class NotificationDeliveryClient(
    HttpClient httpClient,
    ServiceTokenCache tokenCache,
    ILogger<NotificationDeliveryClient> logger
) : INotificationDeliveryClient
{
    private const string ClientId = "auth-notification-delivery";

    public async Task<NotificationEmailLookup> GetEmailStatusAsync(Guid tenantId, Guid eventId, CancellationToken ct)
    {
        try
        {
            var token = await tokenCache.GetOrCreateAsync(
                tenantId,
                ClientId,
                permissions: [],
                scopes: [],
                audience: "TaxVision.Services",
                lifetimeMinutes: 2,
                ct
            );
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/tenants/{tenantId:D}/notification-logs/by-event/{eventId:D}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var response = await httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotificationEmailLookup.NotFound;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Notification delivery status returned {StatusCode} for event {EventId}.",
                    (int)response.StatusCode,
                    eventId
                );
                return NotificationEmailLookup.Unavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<StatusDto>(ct);
            return string.IsNullOrWhiteSpace(body?.Status)
                ? NotificationEmailLookup.Unavailable
                : NotificationEmailLookup.Found(body.Status);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Notification delivery status unavailable for event {EventId}.", eventId);
            return NotificationEmailLookup.Unavailable;
        }
    }

    private sealed record StatusDto(string? Status);
}
