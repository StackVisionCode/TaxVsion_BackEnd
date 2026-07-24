using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Infrastructure.Growth;

/// <summary>
/// Implementación de <see cref="IReferralBenefitReserver"/> contra
/// <c>POST internal/codes/benefit-gifts/reserve</c> de Growth.Api (M2M, sin token de código en
/// texto plano — ver la nota de diseño en el controller de Growth). Nunca lanza: cualquier
/// fallo (red, credenciales, 5xx) se traduce a null, y el caller (ActivateSubscriptionHandler)
/// simplemente cobra el precio completo sin descuento.
/// </summary>
internal sealed class GrowthRefereeBenefitClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    ILogger<GrowthRefereeBenefitClient> logger
) : IReferralBenefitReserver
{
    private const string OfferOwner = "Subscription";
    private const string OfferVersion = "v1";
    private const int QuoteTtlSeconds = 300;
    private const int ReservationTtlSeconds = 3600;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<ReferralBenefitReservation?> TryReserveAsync(
        Guid tenantId,
        string offerId,
        long grossAmountCents,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return null;

        var paymentId = DeterministicGuid($"{idempotencyKey}:referral-benefit-payment");
        var snapshotHash = ComputeSnapshotHash(offerId, grossAmountCents, currency, idempotencyKey);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "internal/codes/benefit-gifts/reserve")
            {
                Content = JsonContent.Create(
                    new
                    {
                        offerOwner = OfferOwner,
                        offerId,
                        offerVersion = OfferVersion,
                        grossAmountCents,
                        currency,
                        snapshotHash,
                        quoteTtlSeconds = QuoteTtlSeconds,
                        paymentSource = "PaymentApp",
                        paymentId,
                        reservationTtlSeconds = ReservationTtlSeconds,
                    },
                    options: Json
                ),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Idempotency-Key", idempotencyKey);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Growth benefit-gift reservation call failed ({Status}).", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<ReserveBenefitGiftResponseDto>(Json, ct);
            if (payload is null || !payload.Found || payload.CodeReservationId is null)
                return null;

            return new ReferralBenefitReservation(
                payload.CodeReservationId.Value,
                paymentId,
                payload.GrossAmountCents ?? grossAmountCents,
                payload.DiscountAmountCents ?? 0,
                payload.NetAmountCents ?? grossAmountCents,
                payload.Currency ?? currency,
                snapshotHash
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Growth benefit-gift reservation call threw for tenant {TenantId}.", tenantId);
            return null;
        }
    }

    /// <summary>Same (idempotencyKey) always yields the same PaymentId, so retrying the whole
    /// activation with the same key re-reserves/replays against the same Codes reservation
    /// instead of drifting to a new one.</summary>
    private static Guid DeterministicGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash[..16]);
    }

    private static string ComputeSnapshotHash(
        string offerId,
        long grossAmountCents,
        string currency,
        string idempotencyKey
    )
    {
        var canonical = $"{offerId}|{grossAmountCents}|{currency.Trim().ToUpperInvariant()}|{idempotencyKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }

    private sealed record ReserveBenefitGiftResponseDto(
        bool Found,
        Guid? CodeReservationId,
        Guid? CodeDefinitionId,
        long? GrossAmountCents,
        long? DiscountAmountCents,
        long? NetAmountCents,
        string? Currency,
        DateTime? ExpiresAtUtc
    );
}
