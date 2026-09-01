using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Api.Requests;

/// <summary>
/// <paramref name="ReturnUrl"/> = origen del frontend (window.location.origin del subdominio del
/// tenant); el callback de OAuth devuelve el navegador ahí. Opcional: si no viene, el controller cae
/// al header Origin, y en última instancia el callback usa el BaseUrl configurado.
/// </summary>
public sealed record InitiateOAuthConnectRequest(ProviderCode ProviderCode, string? ReturnUrl = null);
