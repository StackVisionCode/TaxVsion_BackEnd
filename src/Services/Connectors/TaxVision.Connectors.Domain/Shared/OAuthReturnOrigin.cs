using BuildingBlocks.Platform;
using BuildingBlocks.Results;

namespace TaxVision.Connectors.Domain.Shared;

/// <summary>
/// A dónde vuelve el navegador tras el consentimiento: solo <c>scheme://host[:puerto]</c> de un host
/// permitido (el subdominio de la oficina, o localhost con su puerto cuando el BaseUrl también es local).
/// Sin usuario, ruta ni query: así nada extra viaja dentro del state.
/// </summary>
public sealed record OAuthReturnOrigin
{
    public const string NotAllowedCode = "Connectors.OAuth.ReturnOriginNotAllowed";

    public string Value { get; }

    private OAuthReturnOrigin(string value) => Value = value;

    public static Result<OAuthReturnOrigin> Create(string? raw, string baseUrl, string platformDomain)
    {
        if (
            !Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.UserInfo.Length > 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || !OAuthReturnRedirectPolicy.IsAllowedHost(uri.Host, baseUrl, platformDomain)
        )
            return Result.Failure<OAuthReturnOrigin>(
                new Error(NotAllowedCode, "The return URL must be the origin of this office's site.")
            );

        return Result.Success(new OAuthReturnOrigin(uri.GetLeftPart(UriPartial.Authority)));
    }

    public override string ToString() => Value;
}
