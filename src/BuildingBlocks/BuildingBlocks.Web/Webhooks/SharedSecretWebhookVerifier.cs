using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web.Webhooks;

/// <summary>
/// Webhooks anónimos de proveedores que no firman (ej. SMTP2GO): se protegen con un secreto compartido.
/// Se lee de Bearer, luego <c>X-Webhook-Secret</c>, luego <c>?key=</c> (legado). Nunca se loguea el valor.
/// </summary>
public static class SharedSecretWebhookVerifier
{
    public const string HeaderName = "X-Webhook-Secret";
    private const string BearerPrefix = "Bearer ";

    public static WebhookSecretCheck Verify(HttpRequest request, string? expectedSecret)
    {
        var (provided, source) = ReadProvidedSecret(request);
        if (string.IsNullOrWhiteSpace(expectedSecret))
            return new WebhookSecretCheck(WebhookSecretVerdict.NotConfigured, source);

        return new WebhookSecretCheck(
            Matches(expectedSecret, provided) ? WebhookSecretVerdict.Accepted : WebhookSecretVerdict.Rejected,
            source
        );
    }

    /// <summary>Compara hashes de igual largo en tiempo constante: no deja adivinar el secreto por tiempos.</summary>
    public static bool Matches(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(provided))
            return false;

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }

    private static (string? Secret, WebhookSecretSource Source) ReadProvidedSecret(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return (auth[BearerPrefix.Length..].Trim(), WebhookSecretSource.Bearer);

        var header = request.Headers[HeaderName].ToString().Trim();
        if (!string.IsNullOrEmpty(header))
            return (header, WebhookSecretSource.Header);

        var query = request.Query["key"].ToString().Trim();
        return string.IsNullOrEmpty(query) ? (null, WebhookSecretSource.None) : (query, WebhookSecretSource.Query);
    }
}
