using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.TenantDomains;

/// <summary>
/// Slug de subdominio de una oficina (ej. "oficina1" en oficina1.taxprocore.com).
/// Valida formato de etiqueta DNS y rechaza los nombres reservados de plataforma.
/// La unicidad real (contra otros tenants) la garantiza el índice único de BD + el
/// endpoint de disponibilidad (Fase A4) — este VO solo valida forma, no unicidad.
/// </summary>
public sealed record SubdomainSlug
{
    // Etiqueta DNS: minúsculas/dígitos/guiones, sin guion inicial/final, 3-63 chars.
    private static readonly Regex DnsLabel = new(
        @"^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    // Subdominios de sistema/branding que ninguna oficina puede reclamar.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "www",
        "www2",
        "api",
        "admin",
        "administrator",
        "app",
        "apps",
        "auth",
        "login",
        "account",
        "accounts",
        "billing",
        "mail",
        "smtp",
        "pop",
        "imap",
        "ftp",
        "cdn",
        "assets",
        "static",
        "blog",
        "help",
        "support",
        "docs",
        "status",
        "dashboard",
        "portal",
        "dev",
        "staging",
        "test",
        "beta",
        "demo",
        "secure",
        "ssl",
        "ns",
        "ns1",
        "ns2",
        "mx",
        "email",
        "webmail",
        "cpanel",
        "root",
        "system",
        "internal",
        "api-docs",
        "oauth",
        "sso",
        "id",
        "files",
        "media",
        "img",
        "images",
        "turn",
        "platform",
        "platform-internal",
    };

    private SubdomainSlug(string value) => Value = value;

    public string Value { get; }

    public static Result<SubdomainSlug> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normalized.Length is < 3 or > 63)
        {
            return Result.Failure<SubdomainSlug>(
                new Error("TenantDomain.SlugLength", "Subdomain must be between 3 and 63 characters.")
            );
        }

        if (normalized.StartsWith("xn--", StringComparison.Ordinal))
        {
            return Result.Failure<SubdomainSlug>(
                new Error("TenantDomain.SlugInvalid", "Subdomain cannot use the punycode prefix 'xn--'.")
            );
        }

        if (!DnsLabel.IsMatch(normalized))
        {
            return Result.Failure<SubdomainSlug>(
                new Error(
                    "TenantDomain.SlugInvalid",
                    "Subdomain must contain only lowercase letters, digits and hyphens, "
                        + "and cannot start or end with a hyphen."
                )
            );
        }

        if (Reserved.Contains(normalized))
        {
            return Result.Failure<SubdomainSlug>(
                new Error("TenantDomain.SlugReserved", "This subdomain is reserved and cannot be used.")
            );
        }

        return Result.Success(new SubdomainSlug(normalized));
    }
}
