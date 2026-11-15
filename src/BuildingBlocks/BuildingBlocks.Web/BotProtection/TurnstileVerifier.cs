using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Web.BotProtection;

/// <summary>
/// Llama a <c>siteverify</c> de Cloudflare. Pasa solo con <c>success</c>, la acción esperada y, si se
/// configuraron, uno de los hosts esperados. Cualquier otra respuesta o falla de red es un no.
/// </summary>
public sealed class TurnstileVerifier(HttpClient httpClient, IOptions<TurnstileOptions> options) : ITurnstileVerifier
{
    // Tope documentado por Cloudflare para el token.
    private const int MaxTokenLength = 2048;

    private readonly TurnstileOptions _options = options.Value;

    public bool IsReady =>
        !string.IsNullOrWhiteSpace(_options.SiteKey)
        && !string.IsNullOrWhiteSpace(_options.SecretKey)
        && !string.IsNullOrWhiteSpace(_options.ExpectedAction)
        && Uri.TryCreate(_options.SiteVerifyUrl, UriKind.Absolute, out _);

    public string? SiteKey => IsReady ? _options.SiteKey : null;

    public async Task<TurnstileOutcome> VerifyAsync(string? token, string? clientIp, CancellationToken ct = default)
    {
        if (!IsReady)
            return TurnstileOutcome.NotConfigured;
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
            return TurnstileOutcome.Rejected;

        var form = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey!,
            ["response"] = token,
            ["idempotency_key"] = Guid.NewGuid().ToString("D"),
        };
        if (!string.IsNullOrWhiteSpace(clientIp))
            form["remoteip"] = clientIp;

        SiteVerifyResponse? result;
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClient.PostAsync(_options.SiteVerifyUrl, content, ct);
            if (!response.IsSuccessStatusCode)
                return TurnstileOutcome.Unavailable;
            result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken: ct);
        }
        catch (HttpRequestException)
        {
            return TurnstileOutcome.Unavailable;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return TurnstileOutcome.Unavailable;
        }
        catch (JsonException)
        {
            return TurnstileOutcome.Rejected;
        }

        return IsAccepted(result) ? TurnstileOutcome.Passed : TurnstileOutcome.Rejected;
    }

    private bool IsAccepted(SiteVerifyResponse? result)
    {
        if (result is not { Success: true })
            return false;

        // Las claves de prueba de Cloudflare no devuelven acción ni host y siempre pasan.
        if (result.Metadata?.ResultWithTestingKey == true)
            return _options.AllowTestingKeys;

        if (!string.Equals(result.Action, _options.ExpectedAction, StringComparison.Ordinal))
            return false;

        if (_options.ExpectedHostnames.Count == 0)
            return true;
        foreach (var hostname in _options.ExpectedHostnames)
        {
            if (string.Equals(hostname, result.Hostname, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private sealed record SiteVerifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("hostname")] string? Hostname,
        [property: JsonPropertyName("metadata")] SiteVerifyMetadata? Metadata
    );

    private sealed record SiteVerifyMetadata(
        [property: JsonPropertyName("result_with_testing_key")] bool? ResultWithTestingKey
    );
}
