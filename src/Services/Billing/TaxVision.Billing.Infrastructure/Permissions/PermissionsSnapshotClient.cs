using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Permissions;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Infrastructure.ServiceAuth;

namespace TaxVision.Billing.Infrastructure.Permissions;

/// <summary>
/// Opción B (recuperación pull bajo demanda) — cliente M2M hacia el endpoint interno de Auth
/// (<c>InternalPermissionsSnapshotController</c>). Sin esto, Billing quedaba fail-closed permanente:
/// su tabla de proyección nunca se pobló porque solo la alimentan los eventos de cambio de rol, y un
/// usuario que no cambia de rol no genera ninguno.
///
/// <para>
/// Toma <see cref="IServiceTokenProvider"/> con el nombre de cliente explícito, no el
/// <c>IServiceTokenAcquirer</c> compartido: Billing ya lo tiene registrado apuntando a
/// <c>SubscriptionServiceTokenAcquirer</c> (cliente "Subscription", de RateLimit Fase 2), así que
/// inyectarlo daría el token equivocado.
/// </para>
///
/// <para>
/// Usa un cliente M2M propio (<c>billing-worker</c>, audiencia <c>TaxVision.Services</c>) y no los
/// dos que Billing ya tenía: <c>billing-documents</c> y <c>billing-payments</c> se emiten con
/// audiencia acotada al servicio destino (<c>taxvision-documents</c> / <c>taxvision-payments</c>),
/// así que Auth los rechaza con 401 al validar sus propios endpoints. Medido en vivo antes de
/// registrar el cliente nuevo.
/// </para>
/// </summary>
internal sealed class PermissionsSnapshotClient(
    HttpClient httpClient,
    IServiceTokenProvider tokenProvider,
    ILogger<PermissionsSnapshotClient> logger
) : IPermissionsSnapshotClient
{
    private const string ClientName = "Auth";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<RemotePermissionsSnapshot?> FetchSnapshotAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var token = await tokenProvider.GetTokenAsync(ClientName, tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/tenants/{tenantId:D}/users/{userId:D}/permissions-snapshot"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Permissions snapshot pull for user {UserId} in tenant {TenantId} returned {Status}.",
                    userId,
                    tenantId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<SnapshotDto>(Json, ct);
            return dto is null
                ? null
                : new RemotePermissionsSnapshot(dto.PermissionsVersion, dto.PermissionCodes, dto.RoleIds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Permissions snapshot pull call threw.");
            return null;
        }
    }

    private sealed record SnapshotDto(
        int PermissionsVersion,
        IReadOnlyList<string> PermissionCodes,
        IReadOnlyList<Guid> RoleIds
    );
}
