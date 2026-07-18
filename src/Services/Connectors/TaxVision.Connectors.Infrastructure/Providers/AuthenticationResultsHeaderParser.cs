using System.Text.RegularExpressions;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.Providers;

/// <summary>
/// Parsea el header estándar <c>Authentication-Results</c> (RFC 8601) — mismo formato en Gmail y
/// Graph, de ahí vivir compartido entre los 2 clients en vez de duplicarlo.
/// </summary>
public static partial class AuthenticationResultsHeaderParser
{
    public static AuthenticationSignals Parse(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            return AuthenticationSignals.Unknown;

        return new AuthenticationSignals(
            ExtractResult(headerValue, SpfPattern()),
            ExtractResult(headerValue, DkimPattern()),
            ExtractResult(headerValue, DmarcPattern())
        );
    }

    private static AuthenticationResult ExtractResult(string headerValue, Regex pattern)
    {
        var match = pattern.Match(headerValue);
        if (!match.Success)
            return AuthenticationResult.None;

        return match.Groups[1].Value.ToLowerInvariant() switch
        {
            "pass" => AuthenticationResult.Pass,
            "fail" => AuthenticationResult.Fail,
            _ => AuthenticationResult.Unknown,
        };
    }

    [GeneratedRegex(@"spf=(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex SpfPattern();

    [GeneratedRegex(@"dkim=(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex DkimPattern();

    [GeneratedRegex(@"dmarc=(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex DmarcPattern();
}
