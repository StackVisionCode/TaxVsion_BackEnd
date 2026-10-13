using System.Text.RegularExpressions;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.Platform;

/// <summary>
/// Marca y dominio de la PLATAFORMA (no de una oficina): nombre comercial, razón social, dominio base,
/// host de la API y correos de contacto. Una sola fila. Es la fuente que los demás servicios leen
/// para componer links, asuntos y PDFs; cada cambio sube <see cref="Version"/> y se publica.
///
/// <para>Cambiar <see cref="BaseDomain"/> o <see cref="ApiHost"/> acá solo mueve lo que la aplicación
/// arma (links, resolución de oficinas). DNS, certificados y las URLs registradas en proveedores
/// tienen que apuntar al dominio nuevo ANTES; si no, los links salen a un host que no responde.</para>
/// </summary>
public sealed partial class PlatformSettings : BaseEntity
{
    public const int MaxBrandNameLength = 60;
    public const int MaxLegalNameLength = 120;
    public const int MaxHostLength = 253;
    public const int MaxEmailLength = 254;

    /// <summary>Id fijo de la única fila.</summary>
    public static readonly Guid SingletonId = new("6c1f3d2a-9b4e-4f7a-8d21-5e0c7a9b3f10");

    private PlatformSettings() { }

    public string BrandName { get; private set; } = default!;
    public string LegalName { get; private set; } = default!;
    public string BaseDomain { get; private set; } = default!;
    public string ApiHost { get; private set; } = default!;
    public string SupportEmail { get; private set; } = default!;
    public string BillingEmail { get; private set; } = default!;
    public long Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    public static Result<PlatformSettings> Create(
        string brandName,
        string legalName,
        string baseDomain,
        string apiHost,
        string supportEmail,
        string billingEmail,
        DateTime nowUtc
    )
    {
        var values = Validate(brandName, legalName, baseDomain, apiHost, supportEmail, billingEmail);
        if (values.IsFailure)
            return Result.Failure<PlatformSettings>(values.Error);

        var settings = new PlatformSettings { Id = SingletonId };
        settings.Apply(values.Value, nowUtc, actorId: null);
        return Result.Success(settings);
    }

    /// <summary>
    /// Reemplaza los valores. Devuelve <c>true</c> si algo cambió (y subió la versión); <c>false</c> si
    /// llegó exactamente lo mismo, para no publicar un evento que no cambia nada.
    /// </summary>
    public Result<bool> Update(
        string brandName,
        string legalName,
        string baseDomain,
        string apiHost,
        string supportEmail,
        string billingEmail,
        Guid actorId,
        DateTime nowUtc
    )
    {
        var values = Validate(brandName, legalName, baseDomain, apiHost, supportEmail, billingEmail);
        if (values.IsFailure)
            return Result.Failure<bool>(values.Error);

        var next = values.Value;
        if (
            next.BrandName == BrandName
            && next.LegalName == LegalName
            && next.BaseDomain == BaseDomain
            && next.ApiHost == ApiHost
            && next.SupportEmail == SupportEmail
            && next.BillingEmail == BillingEmail
        )
            return Result.Success(false);

        Apply(next, nowUtc, actorId);
        return Result.Success(true);
    }

    private void Apply(ValidatedValues values, DateTime nowUtc, Guid? actorId)
    {
        BrandName = values.BrandName;
        LegalName = values.LegalName;
        BaseDomain = values.BaseDomain;
        ApiHost = values.ApiHost;
        SupportEmail = values.SupportEmail;
        BillingEmail = values.BillingEmail;
        Version++;
        UpdatedAtUtc = nowUtc;
        UpdatedByUserId = actorId;
    }

    private static Result<ValidatedValues> Validate(
        string brandName,
        string legalName,
        string baseDomain,
        string apiHost,
        string supportEmail,
        string billingEmail
    )
    {
        var brand = brandName?.Trim() ?? string.Empty;
        if (brand.Length is < 2 or > MaxBrandNameLength)
            return Invalid("PlatformSettings.BrandName", $"Brand name must be 2-{MaxBrandNameLength} characters.");

        var legal = legalName?.Trim() ?? string.Empty;
        if (legal.Length is < 2 or > MaxLegalNameLength)
            return Invalid("PlatformSettings.LegalName", $"Legal name must be 2-{MaxLegalNameLength} characters.");

        var domain = NormalizeHost(baseDomain);
        if (!IsHost(domain))
            return Invalid("PlatformSettings.BaseDomain", "Base domain must be a valid domain, like taxoffix.io.");

        var api = NormalizeHost(apiHost);
        if (!IsHost(api))
            return Invalid("PlatformSettings.ApiHost", "API host must be a valid host, like api.taxoffix.io.");

        var support = supportEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IsEmail(support))
            return Invalid("PlatformSettings.SupportEmail", "Support email must be a valid email address.");

        var billing = billingEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IsEmail(billing))
            return Invalid("PlatformSettings.BillingEmail", "Billing email must be a valid email address.");

        return Result.Success(new ValidatedValues(brand, legal, domain, api, support, billing));
    }

    private static Result<ValidatedValues> Invalid(string code, string message) =>
        Result.Failure<ValidatedValues>(new Error(code, message));

    private static string NormalizeHost(string? host) => host?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty;

    private static bool IsHost(string host) => host.Length <= MaxHostLength && HostPattern().IsMatch(host);

    private static bool IsEmail(string email) => email.Length <= MaxEmailLength && EmailPattern().IsMatch(email);

    // Etiquetas DNS de 1-63 caracteres, al menos dos, TLD alfabético. Sin esquema ni ruta.
    [GeneratedRegex(@"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex HostPattern();

    [GeneratedRegex(@"^[^@\s]+@(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex EmailPattern();

    private sealed record ValidatedValues(
        string BrandName,
        string LegalName,
        string BaseDomain,
        string ApiHost,
        string SupportEmail,
        string BillingEmail
    );
}
