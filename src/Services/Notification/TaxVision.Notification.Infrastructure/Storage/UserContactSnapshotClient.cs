using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Storage;

/// <summary>
/// Recuperación pull del correo de un usuario contra el endpoint interno de Auth
/// (<c>internal/tenants/{tenantId}/users/{userId}/contact</c>). Reusa el <see cref="IServiceTokenAcquirer"/>
/// que ya existía en este servicio — un acquirer por servicio, un HttpClient tipado por destino.
///
/// <para>
/// Nunca lanza: cualquier fallo de token, HTTP o 404 devuelve <c>null</c> y el correo simplemente no
/// se manda. Mismo criterio que <c>PermissionsSnapshotClient</c>: una notificación no entregada es
/// mejor que un consumer que revienta y se reintenta contra la DLQ.
/// </para>
/// </summary>
public sealed class UserContactSnapshotClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<UserContactSnapshotClient> logger
) : IUserContactSnapshotClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<RemoteUserContact?> FetchContactAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/tenants/{tenantId:D}/users/{userId:D}/contact"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Contact pull for user {UserId} in tenant {TenantId} returned {Status}.",
                    userId,
                    tenantId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<ContactDto>(Json, ct);
            return dto is null || string.IsNullOrWhiteSpace(dto.Email)
                ? null
                : new RemoteUserContact(dto.Email, dto.IsActive);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Contact pull call threw.");
            return null;
        }
    }

    private sealed record ContactDto(string Email, string ActorType, bool IsActive);
}
