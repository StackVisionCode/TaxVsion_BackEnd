using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Common;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;
using Wolverine;

namespace TaxVision.Connectors.Api.Controllers;

/// <summary>
/// Google/Microsoft redirigen el navegador acá tras el consentimiento (D3 §12.4) — público, no hay
/// forma de mandar un Bearer token en un redirect de 302. La identidad del caller viene enteramente
/// de <c>state</c> (CSRF, un solo uso, ver <see cref="IOAuthConnectStateStore"/>), nunca de query
/// params del proveedor. Siempre termina en una redirección al frontend, nunca en un JSON de error —
/// esta ruta la visita el navegador del usuario, no un fetch.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("connectors/oauth/callback")]
public sealed class OAuthCallbackController(
    IMessageBus bus,
    IOAuthConnectStateStore stateStore,
    IOptions<ConnectorsPortalOptions> portalOptions,
    ILogger<OAuthCallbackController> logger
) : ControllerBase
{
    [HttpGet("gmail")]
    public Task<IActionResult> Gmail(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct
    ) => HandleAsync(ProviderCode.Gmail, code, state, error, ct);

    [HttpGet("graph")]
    public Task<IActionResult> Graph(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken ct
    ) => HandleAsync(ProviderCode.Graph, code, state, error, ct, errorDescription);

    private async Task<IActionResult> HandleAsync(
        ProviderCode providerCode,
        string? code,
        string? state,
        string? error,
        CancellationToken ct,
        string? errorDescription = null
    )
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            // AADSTS90094/consent_required: la política del tenant de Microsoft bloqueó el
            // consentimiento delegado — es el disparador real para ofrecer el fallback de
            // admin-consent (D3 §12.6), no un error genérico.
            logger.LogInformation(
                "OAuth connect callback ({Provider}) returned an error: {Error} {Description}",
                providerCode,
                error,
                errorDescription
            );
            return Redirect(BuildRedirect("connectors_error", error));
        }

        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
            return Redirect(BuildRedirect("connectors_error", "invalid_request"));

        var connectState = await stateStore.ConsumeAsync(state, ct);
        if (connectState is null)
            return Redirect(BuildRedirect("connectors_error", "invalid_state"));

        if (connectState.ProviderCode != providerCode)
            return Redirect(BuildRedirect("connectors_error", "invalid_state"));

        var result = await bus.InvokeAsync<Result<CompleteOAuthConnectResult>>(
            new CompleteOAuthConnectCommand(connectState.TenantId, providerCode, connectState.InitiatedByUserId, code),
            ct
        );

        if (result.IsFailure)
        {
            logger.LogWarning(
                "OAuth connect exchange failed ({Provider}): {Code} {Message}",
                providerCode,
                result.Error.Code,
                result.Error.Message
            );
            return Redirect(BuildRedirect("connectors_error", "exchange_failed"));
        }

        return Redirect($"{BuildRedirect("connectors_connected", "true")}&accountId={result.Value.AccountId}");
    }

    /// <summary>
    /// Admin-consent fallback (D3 §12.6) — el admin del tenant de Microsoft vuelve acá tras otorgar
    /// (o rechazar) el consentimiento a nivel organización. No hay nada que persistir del lado de
    /// Connectors: el consentimiento ya quedó registrado en Entra ID, esto solo le confirma al
    /// frontend que puede reintentar el connect normal (D3 §12.4) sin toparse con AADSTS90094.
    /// </summary>
    [HttpGet("~/connectors/oauth/admin-consent-callback")]
    public async Task<IActionResult> AdminConsentCallback(
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "admin_consent")] string? adminConsent,
        CancellationToken ct
    )
    {
        if (!string.IsNullOrWhiteSpace(error))
            return Redirect(BuildRedirect("connectors_error", error));

        if (string.IsNullOrWhiteSpace(state))
            return Redirect(BuildRedirect("connectors_error", "invalid_request"));

        var connectState = await stateStore.ConsumeAsync(state, ct);
        if (connectState is null)
            return Redirect(BuildRedirect("connectors_error", "invalid_state"));

        var granted = string.Equals(adminConsent, "True", StringComparison.OrdinalIgnoreCase);
        return Redirect(BuildRedirect("connectors_admin_consent", granted ? "true" : "false"));
    }

    private string BuildRedirect(string key, string value) =>
        $"{portalOptions.Value.BaseUrl}?{key}={Uri.EscapeDataString(value)}";
}
