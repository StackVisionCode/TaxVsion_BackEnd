using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;

namespace TaxVision.PaymentApp.Application.Common.HostedCheckout;

/// <summary>Valida a dónde vuelve el pagador desde la página del proveedor.</summary>
public interface ICheckoutReturnUrlGuard
{
    Result Validate(string? successUrl, string? cancelUrl);
}

/// <summary>Orígenes extra permitidos además de los de la plataforma (en Development, los localhost).</summary>
public sealed class CheckoutReturnUrlOptions
{
    public const string SectionName = "PaymentApp:CheckoutReturnUrls";

    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>
/// Solo se vuelve a una app de la plataforma: apex, subdominio reservado o de oficina
/// (<c>{slug}.{BaseDomain}</c>), en HTTPS y puerto estándar. Es la misma regla del CORS del Gateway;
/// los dominios propios de oficina entran cuando el Gateway los acepte. Más los orígenes exactos
/// de <see cref="CheckoutReturnUrlOptions"/>.
/// </summary>
public sealed class CheckoutReturnUrlGuard(IPlatformSettings platform, IOptions<CheckoutReturnUrlOptions> options)
    : ICheckoutReturnUrlGuard
{
    public static readonly Error InvalidReturnUrl = new(
        "Payment.InvalidReturnUrl",
        "The return URL must point to a platform app."
    );

    public Result Validate(string? successUrl, string? cancelUrl) =>
        IsAllowed(successUrl) && IsAllowed(cancelUrl) ? Result.Success() : Result.Failure(InvalidReturnUrl);

    private bool IsAllowed(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return false;
        // "https://app.dominio@evil.com" lleva al host de después de la arroba.
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;

        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (PlatformSettingsSnapshot.IsOriginIn(origin, options.Value.AllowedOrigins))
            return true;

        return uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && platform.Current.IsPlatformHttpsOrigin(origin);
    }
}
