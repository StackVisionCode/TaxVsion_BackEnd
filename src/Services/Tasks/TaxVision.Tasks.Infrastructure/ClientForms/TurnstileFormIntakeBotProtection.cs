using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

public sealed class TurnstileFormIntakeBotProtection(
    HttpClient httpClient,
    IOptions<ClientFormsOptions> options
) : IFormIntakeBotProtection
{
    private const int MaxTokenLength = 2048;
    private readonly ClientFormsBotProtectionOptions _options = options.Value.External.BotProtection;

    public bool IsReady =>
        !string.IsNullOrWhiteSpace(_options.SecretKey)
        && Uri.TryCreate(_options.SiteVerifyUrl, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(_options.ExpectedAction);

    public async Task<bool> VerifyAsync(string token, string clientIp, CancellationToken ct = default)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        using var request = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["secret"] = _options.SecretKey!,
                ["response"] = token,
                ["remoteip"] = clientIp,
                ["idempotency_key"] = Guid.NewGuid().ToString("D"),
            }
        );

        try
        {
            using var response = await httpClient.PostAsync(_options.SiteVerifyUrl, request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<TurnstileSiteVerifyResponse>(
                cancellationToken: ct
            );
            return result is { Success: true }
                && string.Equals(result.Action, _options.ExpectedAction, StringComparison.Ordinal);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private sealed record TurnstileSiteVerifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("action")] string? Action
    );
}
