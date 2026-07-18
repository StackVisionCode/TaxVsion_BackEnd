using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.Providers.Watch;

/// <summary>
/// Gmail no tiene un id de suscripción propio — <c>users.watch</c> es un único registro implícito
/// por (cuenta, topic), y renovarlo es literalmente volver a llamar el mismo endpoint (a diferencia
/// de Graph, que sí tiene un id de subscription real que se PATCHea). El historyId devuelto se usa
/// como <c>SubscriptionRef</c> solo para trazabilidad — no hace falta para renovar.
/// </summary>
public sealed class GmailWatchClient(
    HttpClient httpClient,
    IOAuthTokenManager tokenManager,
    IOptions<GmailWatchOptions> options,
    ILogger<GmailWatchClient> logger
) : IWatchProviderClient
{
    private const string WatchUrl = "https://gmail.googleapis.com/gmail/v1/users/me/watch";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    public ProviderCode ProviderCode => ProviderCode.Gmail;

    public Task<WatchSetupResult> SetupWatchAsync(Guid accountId, CancellationToken ct = default) =>
        CallWatchAsync(accountId, ct);

    public Task<WatchSetupResult> RenewWatchAsync(
        Guid accountId,
        string subscriptionRef,
        CancellationToken ct = default
    ) => CallWatchAsync(accountId, ct);

    private async Task<WatchSetupResult> CallWatchAsync(Guid accountId, CancellationToken ct)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.TopicName))
            throw new WatchProviderException("Connectors:Watch:Gmail:TopicName is not configured.");

        var tokenResult = await tokenManager.GetValidAccessTokenAsync(accountId, ct);
        if (tokenResult.IsFailure)
            throw new WatchProviderException($"Could not obtain a valid access token: {tokenResult.Error.Message}");

        using var request = new HttpRequestMessage(HttpMethod.Post, WatchUrl)
        {
            Content = JsonContent.Create(new GmailWatchRequest(config.TopicName, ["INBOX"])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new WatchProviderException("Gmail watch request failed (network error).", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gmail watch request returned HTTP {Status}.", (int)response.StatusCode);
            throw new WatchProviderException($"Gmail watch request returned HTTP {(int)response.StatusCode}.");
        }

        GmailWatchResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<GmailWatchResponse>(ct);
        }
        catch (JsonException ex)
        {
            throw new WatchProviderException("Gmail watch response was unparseable.", ex);
        }

        if (payload is null || string.IsNullOrEmpty(payload.HistoryId))
            throw new WatchProviderException("Gmail watch response was missing historyId.");

        var expiresAtUtc = long.TryParse(payload.ExpirationEpochMillis, out var millis)
            ? DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime
            : DateTime.UtcNow.Add(DefaultLifetime);

        return new WatchSetupResult(payload.HistoryId, config.TopicName, expiresAtUtc);
    }

    private sealed record GmailWatchRequest(
        [property: JsonPropertyName("topicName")] string TopicName,
        [property: JsonPropertyName("labelIds")] string[] LabelIds
    );

    private sealed record GmailWatchResponse
    {
        [JsonPropertyName("historyId")]
        public string? HistoryId { get; init; }

        [JsonPropertyName("expiration")]
        public string? ExpirationEpochMillis { get; init; }
    }
}
