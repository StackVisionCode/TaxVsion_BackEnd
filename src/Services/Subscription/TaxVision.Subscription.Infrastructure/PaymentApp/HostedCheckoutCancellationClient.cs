using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Infrastructure.Growth;

namespace TaxVision.Subscription.Infrastructure.PaymentApp;

/// <summary>HTTP M2M contra <c>POST internal/hosted-checkout/payments/{id}/cancel</c> de PaymentApp. Fail-open:
/// nunca lanza y nunca devuelve Failure que bloquee al caller — el intent local ya está Cancelled.</summary>
internal sealed class HostedCheckoutCancellationClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    ILogger<HostedCheckoutCancellationClient> logger
) : IHostedCheckoutCancellationClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result> CancelAsync(Guid tenantId, Guid saaSPaymentId, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning(
                "No service token to cancel hosted checkout for payment {PaymentId}; local intent is already Cancelled, continuing.",
                saaSPaymentId
            );
            return Result.Success();
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"internal/hosted-checkout/payments/{saaSPaymentId:D}/cancel?tenantId={tenantId:D}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation(
                    "PaymentApp has no hosted session for payment {PaymentId}; nothing to cancel.",
                    saaSPaymentId
                );
                return Result.Success();
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "PaymentApp cancel hosted checkout returned {Status} for payment {PaymentId}; continuing (local intent is Cancelled).",
                    (int)response.StatusCode,
                    saaSPaymentId
                );
                return Result.Success();
            }

            var payload = await response.Content.ReadFromJsonAsync<CancelResponseDto>(Json, ct);
            if (payload is not null && payload.AlreadyExpired)
                logger.LogDebug(
                    "PaymentApp reported hosted session for payment {PaymentId} as already expired.",
                    saaSPaymentId
                );

            return Result.Success();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "PaymentApp cancel hosted checkout threw for payment {PaymentId}; continuing (local intent is Cancelled).",
                saaSPaymentId
            );
            return Result.Success();
        }
    }

    private sealed record CancelResponseDto(bool AlreadyExpired);
}
