using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Staff.Abstractions;

namespace TaxVision.Calendar.Infrastructure.Staff;

/// <summary>
/// Nombre y correo de un empleado desde Auth (<c>internal/tenants/{t}/users/{u}/contact</c>, M2M). Diez
/// minutos en memoria: el portal pregunta por los mismos preparadores en cada vista. Nunca lanza.
/// </summary>
public sealed class StaffContactClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    IMemoryCache cache,
    ILogger<StaffContactClient> logger
) : IStaffContactClient
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private static int _warnedNoToken;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<StaffContact?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var key = $"calendar:staff-contact:{tenantId:N}:{userId:N}";
        if (cache.TryGetValue(key, out StaffContact? cached))
            return cached;

        var contact = await FetchAsync(tenantId, userId, ct);
        if (contact is not null)
            cache.Set(key, contact, Ttl);
        return contact;
    }

    private async Task<StaffContact?> FetchAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
        {
            // Sin credencial M2M (secreto vacío) todo falla igual: se avisa una vez, no en cada vista.
            if (Interlocked.Exchange(ref _warnedNoToken, 1) == 0)
                logger.LogWarning(
                    "Calendar has no M2M token for Auth: staff names will show as a fallback. Check ServiceAuthClient secret."
                );
            return null;
        }

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
                    "Staff contact for {UserId} returned {Status}.",
                    userId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<ContactDto>(Json, ct);
            return dto is null || string.IsNullOrWhiteSpace(dto.Email)
                ? null
                : new StaffContact(dto.Email, dto.DisplayName, dto.IsActive);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Staff contact call threw.");
            return null;
        }
    }

    private sealed record ContactDto(string Email, string ActorType, bool IsActive, string? DisplayName);
}
