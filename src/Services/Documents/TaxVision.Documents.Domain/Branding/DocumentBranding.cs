using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Documents.Domain.Branding;

/// <summary>
/// Perfil de marca del tenant, UNO por tenant (índice único por TenantId). El tenant lo configura una
/// vez y se aplica a sus documentos sin tener que mandarlo en cada request. Solo apariencia: nombre
/// visible, logo, color y pie. No conoce reglas de negocio ni el link de pago (eso es de Billing).
///
/// El logo se guarda como data-URI embebido (`data:image/...`) — un recurso externo lo bloquea el CSP
/// del motor de render y no se imprimiría. Para "libertad total" (que el tenant escriba su propio HTML)
/// está el sistema de plantillas versionadas en BD (fase posterior); esto cubre el branding sin exponer
/// edición de HTML.
/// </summary>
public sealed class DocumentBranding : AggregateRoot
{
    public const int MaxDisplayNameLength = 120;
    public const int MaxFooterLength = 300;
    // ~700 KB de PNG/JPG en base64. Tope defensivo: un logo enorme infla cada PDF y el mensaje del bus.
    public const int MaxLogoDataUriLength = 1_000_000;

    public string? DisplayName { get; private set; }
    public string? LogoDataUri { get; private set; }
    public string? BrandColorHex { get; private set; }
    public string? FooterText { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private DocumentBranding() { }

    public static Result<DocumentBranding> Create(
        Guid tenantId,
        string? displayName,
        string? logoDataUri,
        string? brandColorHex,
        string? footerText,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<DocumentBranding>(new Error("Documents.Branding.InvalidTenant", "TenantId is required."));

        var validated = Validate(displayName, logoDataUri, brandColorHex, footerText);
        if (validated.IsFailure)
            return Result.Failure<DocumentBranding>(validated.Error);

        var branding = new DocumentBranding { CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        branding.SetTenant(tenantId);
        branding.Apply(validated.Value);
        return Result.Success(branding);
    }

    public Result Update(string? displayName, string? logoDataUri, string? brandColorHex, string? footerText, DateTime nowUtc)
    {
        var validated = Validate(displayName, logoDataUri, brandColorHex, footerText);
        if (validated.IsFailure)
            return Result.Failure(validated.Error);

        Apply(validated.Value);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private void Apply(BrandingFields fields)
    {
        DisplayName = fields.DisplayName;
        LogoDataUri = fields.LogoDataUri;
        BrandColorHex = fields.BrandColorHex;
        FooterText = fields.FooterText;
    }

    private static Result<BrandingFields> Validate(
        string? displayName,
        string? logoDataUri,
        string? brandColorHex,
        string? footerText
    )
    {
        displayName = Trim(displayName);
        logoDataUri = Trim(logoDataUri);
        brandColorHex = Trim(brandColorHex);
        footerText = Trim(footerText);

        if (displayName is { Length: > MaxDisplayNameLength })
            return Result.Failure<BrandingFields>(new Error("Documents.Branding.DisplayNameTooLong", $"DisplayName cannot exceed {MaxDisplayNameLength} characters."));

        if (footerText is { Length: > MaxFooterLength })
            return Result.Failure<BrandingFields>(new Error("Documents.Branding.FooterTooLong", $"FooterText cannot exceed {MaxFooterLength} characters."));

        if (brandColorHex is not null && !IsHexColor(brandColorHex))
            return Result.Failure<BrandingFields>(new Error("Documents.Branding.InvalidColor", "BrandColorHex must be a #RGB or #RRGGBB hex color."));

        if (logoDataUri is not null)
        {
            if (!logoDataUri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return Result.Failure<BrandingFields>(new Error("Documents.Branding.InvalidLogo", "LogoDataUri must be an embedded data:image/ URI."));
            if (logoDataUri.Length > MaxLogoDataUriLength)
                return Result.Failure<BrandingFields>(new Error("Documents.Branding.LogoTooLarge", "LogoDataUri is too large."));
        }

        return Result.Success(new BrandingFields(displayName, logoDataUri, brandColorHex, footerText));
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsHexColor(string value) =>
        value.Length is 4 or 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    private readonly record struct BrandingFields(string? DisplayName, string? LogoDataUri, string? BrandColorHex, string? FooterText);
}
