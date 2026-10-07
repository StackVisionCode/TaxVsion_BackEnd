using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Runs.Abstractions;

namespace TaxVision.Campaigns.Infrastructure.Wallet;

public sealed class WalletServiceOptions
{
    public const string SectionName = "WalletService";

    /// <summary>Base URL del servicio Wallet. En Docker: http://wallet-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5270";
}

/// <summary>
/// Cliente del PEP del Wallet (00_Plan §5). El Wallet es independiente del consumidor; Campaigns lo usa con
/// <c>referenceType = "campaign-run"</c>. Endpoints internos (no por el Gateway):
/// <list type="bullet">
/// <item>Reservar (<c>POST /internal/wallet/reservations</c>): on-behalf-of el bearer del usuario que envía; o
/// M2M del tenant si no hay sesión (scheduler). Fail-closed.</item>
/// <item>Liquidar (<c>POST /internal/wallet/reservations/settle</c>): M2M del tenant (lo dispara un evento sin
/// sesión humana). Reintentable: lanza ante fallo transitorio; no-op si no hay reserva (4xx).</item>
/// </list>
/// </summary>
public sealed class WalletSpendClient(
    HttpClient http,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<WalletSpendClient> logger
) : IWalletSpendClient
{
    public async Task<WalletReserveResult> ReserveAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        WalletUnitCounts units,
        string? callerBearerToken,
        CancellationToken ct = default
    )
    {
        var token = callerBearerToken;
        if (string.IsNullOrWhiteSpace(token))
            token = await tokenAcquirer.GetTokenAsync(tenantId, ct); // ruta scheduler (sin sesión humana)
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("No credential to reserve funds for {RefType}:{RefId} (tenant {TenantId}).", referenceType, referenceId, tenantId);
            return WalletReserveResult.Unreachable();
        }

        var payload = new ReservePayload(referenceType, referenceId, units.Email, units.Sms, units.Push, units.WhatsApp);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "internal/wallet/reservations")
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Wallet reserve returned {Status} for {RefType}:{RefId} (tenant {TenantId}); blocking send.",
                    (int)response.StatusCode,
                    referenceType,
                    referenceId,
                    tenantId
                );
                return WalletReserveResult.Unreachable();
            }

            var dto = await response.Content.ReadFromJsonAsync<ReserveResponse>(ct);
            if (dto is null)
                return WalletReserveResult.Unreachable();

            return new WalletReserveResult(true, dto.Authorized, dto.CostMicros, dto.AvailableMicros, dto.DeficitMicros, dto.Currency ?? "USD");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not reach Wallet to reserve for {RefType}:{RefId} (tenant {TenantId}).", referenceType, referenceId, tenantId);
            return WalletReserveResult.Unreachable();
        }
    }

    public async Task SettleAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        int consumedUnits,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"No M2M token to settle {referenceType}:{referenceId} (tenant {tenantId}).");

        var payload = new SettlePayload(referenceType, referenceId, consumedUnits);
        using var request = new HttpRequestMessage(HttpMethod.Post, "internal/wallet/reservations/settle")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return;

        // 4xx (p.ej. referencia sin reserva = run de costo 0): terminal, no reintentar.
        if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning(
                "Wallet settle returned {Status} for {RefType}:{RefId} (tenant {TenantId}); treating as no-op.",
                (int)response.StatusCode,
                referenceType,
                referenceId,
                tenantId
            );
            return;
        }

        // 5xx / 429 / transporte: transitorio → lanza para que Wolverine reintente (la reserva sigue viva).
        throw new InvalidOperationException(
            $"Wallet settle failed ({(int)response.StatusCode}) for {referenceType}:{referenceId} (tenant {tenantId})."
        );
    }

    private sealed record ReservePayload(string ReferenceType, Guid ReferenceId, long Email, long Sms, long Push, long WhatsApp);

    private sealed record SettlePayload(string ReferenceType, Guid ReferenceId, int ConsumedUnits);

    private sealed record ReserveResponse(
        bool Authorized,
        Guid? ReservationId,
        int PriceBookVersion,
        long CostMicros,
        long AvailableMicros,
        long DeficitMicros,
        string? Currency
    );
}
