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
/// Graph subscriptions sobre mensajes tienen un máximo de vida de ~4230 minutos (~2.9 días) —
/// bastante menos que Gmail. <c>resource</c> queda fijo en <c>mailFolders('inbox')/messages</c>
/// (D1, §34.5: nunca <c>/me/messages</c>, que cubriría el mailbox completo).
/// </summary>
public sealed class GraphWatchClient(
    HttpClient httpClient,
    IOAuthTokenManager tokenManager,
    IOptions<GraphWatchOptions> options,
    ILogger<GraphWatchClient> logger
) : IWatchProviderClient
{
    private const string SubscriptionsUrl = "https://graph.microsoft.com/v1.0/subscriptions";
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(70);

    public ProviderCode ProviderCode => ProviderCode.Graph;

    public Task<WatchSetupResult> SetupWatchAsync(Guid accountId, CancellationToken ct = default) =>
        CreateSubscriptionAsync(accountId, ct);

    public async Task<WatchSetupResult> RenewWatchAsync(
        Guid accountId,
        string subscriptionRef,
        CancellationToken ct = default
    )
    {
        var tokenResult = await tokenManager.GetValidAccessTokenAsync(accountId, ct);
        if (tokenResult.IsFailure)
            throw new WatchProviderException($"Could not obtain a valid access token: {tokenResult.Error.Message}");

        var newExpiration = DateTime.UtcNow.Add(MaxLifetime);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{SubscriptionsUrl}/{subscriptionRef}")
        {
            Content = JsonContent.Create(new GraphSubscriptionPatchRequest(newExpiration)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);

        var response = await SendAsync(request, ct);
        var payload = await ReadResponseAsync(response, ct);
        return new WatchSetupResult(payload.Id ?? subscriptionRef, null, payload.ExpirationDateTime ?? newExpiration);
    }

    private async Task<WatchSetupResult> CreateSubscriptionAsync(Guid accountId, CancellationToken ct)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.NotificationUrl) || string.IsNullOrWhiteSpace(config.ClientState))
            throw new WatchProviderException("Connectors:Watch:Graph:NotificationUrl/ClientState is not configured.");

        var tokenResult = await tokenManager.GetValidAccessTokenAsync(accountId, ct);
        if (tokenResult.IsFailure)
            throw new WatchProviderException($"Could not obtain a valid access token: {tokenResult.Error.Message}");

        var expiration = DateTime.UtcNow.Add(MaxLifetime);
        using var request = new HttpRequestMessage(HttpMethod.Post, SubscriptionsUrl)
        {
            Content = JsonContent.Create(
                new GraphSubscriptionCreateRequest(
                    "created",
                    config.NotificationUrl,
                    "me/mailFolders('inbox')/messages",
                    expiration,
                    config.ClientState
                )
            ),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);

        var response = await SendAsync(request, ct);
        var payload = await ReadResponseAsync(response, ct);
        if (string.IsNullOrEmpty(payload.Id))
            throw new WatchProviderException("Graph subscription response was missing id.");

        return new WatchSetupResult(payload.Id, null, payload.ExpirationDateTime ?? expiration);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new WatchProviderException("Graph subscription request failed (network error).", ex);
        }
    }

    private async Task<GraphSubscriptionResponse> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Graph subscription request returned HTTP {Status}.", (int)response.StatusCode);
            throw new WatchProviderException($"Graph subscription request returned HTTP {(int)response.StatusCode}.");
        }

        try
        {
            var payload = await response.Content.ReadFromJsonAsync<GraphSubscriptionResponse>(ct);
            return payload ?? throw new WatchProviderException("Graph subscription response was empty.");
        }
        catch (JsonException ex)
        {
            throw new WatchProviderException("Graph subscription response was unparseable.", ex);
        }
    }

    private sealed record GraphSubscriptionCreateRequest(
        [property: JsonPropertyName("changeType")] string ChangeType,
        [property: JsonPropertyName("notificationUrl")] string NotificationUrl,
        [property: JsonPropertyName("resource")] string Resource,
        [property: JsonPropertyName("expirationDateTime")] DateTime ExpirationDateTime,
        [property: JsonPropertyName("clientState")] string ClientState
    );

    private sealed record GraphSubscriptionPatchRequest(
        [property: JsonPropertyName("expirationDateTime")] DateTime ExpirationDateTime
    );

    private sealed record GraphSubscriptionResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("expirationDateTime")]
        public DateTime? ExpirationDateTime { get; init; }
    }
}
