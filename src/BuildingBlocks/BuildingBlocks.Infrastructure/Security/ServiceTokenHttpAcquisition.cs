using System.Net.Http.Json;
using System.Text.Json;

namespace BuildingBlocks.Infrastructure.Security;

/// <summary>
/// F25 — extrae el POST+parse común a los ~9 acquirers de token M2M por servicio (Tenant, Scribe,
/// Signature, Correspondence, Customer, Notification, Postmaster, Subscription/Growth, PaymentApp),
/// todos byte-por-byte idénticos en forma: <c>POST auth/service-token</c> con
/// <c>{clientId, clientSecret, tenantId}</c>, respuesta <c>{accessToken, expiresInSeconds, tokenType?}</c>.
/// Cada acquirer de servicio conserva su propio <c>Task&lt;string?&gt; GetTokenAsync(...)</c> público
/// (contrato sin cambios para sus callers), que atrapa <see cref="ServiceTokenAcquisitionException"/>
/// y devuelve <c>null</c> — este helper solo hace la llamada HTTP y lanza en caso de fallo.
/// </summary>
public static class ServiceTokenHttpAcquisition
{
    private sealed record ServiceTokenDto(string AccessToken, int ExpiresInSeconds, string? TokenType);

    /// <exception cref="ServiceTokenAcquisitionException">
    /// Red caída, timeout, o respuesta de Auth no exitosa / no parseable.
    /// </exception>
    public static async Task<ServiceTokenGrant> RequestServiceTokenAsync(
        this HttpClient httpClient,
        string clientId,
        string clientSecret,
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "auth/service-token",
                new
                {
                    clientId,
                    clientSecret,
                    tenantId,
                },
                ct
            );
            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<ServiceTokenDto>(ct);
            if (dto is null)
                throw new ServiceTokenAcquisitionException("Auth returned an empty service-token response.");

            return new ServiceTokenGrant(dto.AccessToken, DateTime.UtcNow.AddSeconds(dto.ExpiresInSeconds));
        }
        // BB-16 — la cancelación del caller NO es un fallo de adquisición: si se traga acá, los
        // acquirers la traducen a `null` y un shutdown del host o una request abortada se reportan
        // como "Auth no respondió". Va antes del catch de abajo porque TaskCanceledException hereda
        // de OperationCanceledException; el filtro por el token es lo que las separa — el timeout
        // propio del HttpClient lanza el mismo tipo **sin** el token cancelado, y ese sí es un fallo.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ServiceTokenAcquisitionException("Could not acquire a service token from Auth.", ex);
        }
    }
}
