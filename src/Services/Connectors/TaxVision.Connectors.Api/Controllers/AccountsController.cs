using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Connectors.Api.Authorization;
using TaxVision.Connectors.Api.Common;
using TaxVision.Connectors.Api.Requests;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Watch;
using Wolverine;

namespace TaxVision.Connectors.Api.Controllers;

[ApiController]
[Route("connectors")]
public sealed class AccountsController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Arranca el flujo de conectar cuenta (D3 §12.4) — el frontend redirige el navegador a
    /// <c>AuthorizationUrl</c>, no hace un fetch normal (el consentimiento vive en Google/Microsoft).
    /// </summary>
    [HttpPost("accounts")]
    [HasPermission(ConnectorsPermissions.AccountsWrite)]
    public async Task<IActionResult> Initiate([FromBody] InitiateOAuthConnectRequest body, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<InitiateOAuthConnectResult>>(
            new InitiateOAuthConnectCommand(tenantId, body.ProviderCode, userId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Conecta una cuenta manual IMAP+SMTP (D3 Compose §8) — a diferencia de <see cref="Initiate"/>, acá no
    /// hay redirección: el frontend manda host/puerto/usuario/contraseña directo y esta acción valida
    /// conectividad real contra ambos servidores antes de persistir nada.
    /// </summary>
    [HttpPost("accounts/manual")]
    [HasPermission(ConnectorsPermissions.AccountsWrite)]
    public async Task<IActionResult> ConnectManual([FromBody] ConnectManualAccountRequest body, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<ConnectManualAccountResult>>(
            new ConnectManualAccountCommand(
                tenantId,
                userId,
                body.EmailAddress,
                body.DisplayName,
                body.ImapHost,
                body.ImapPort,
                body.ImapUseSsl,
                body.ImapUsername,
                body.ImapPassword,
                body.SmtpHost,
                body.SmtpPort,
                body.SmtpUseStartTls,
                body.SmtpUsername,
                body.SmtpPassword
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("accounts")]
    [HasPermission(ConnectorsPermissions.AccountsRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var accounts = await bus.InvokeAsync<IReadOnlyList<TenantEmailAccountDto>>(
            new ListTenantEmailAccountsQuery(tenantId),
            ct
        );
        return Ok(accounts);
    }

    [HttpGet("accounts/{id:guid}")]
    [HasPermission(ConnectorsPermissions.AccountsRead)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TenantEmailAccountDto>>(
            new GetTenantEmailAccountQuery(tenantId, id),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Desconecta la cuenta (D3 §12.4/12.8). Para Graph esto es solo del lado de TaxVision — el
    /// consentimiento en Microsoft sigue vivo hasta que el usuario lo revoque él mismo desde
    /// myaccount.microsoft.com/consents (Graph no expone una API de revocación equivalente).
    /// </summary>
    [HttpDelete("accounts/{id:guid}")]
    [HasPermission(ConnectorsPermissions.AccountsWrite)]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(new DisconnectAccountCommand(tenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Fallback de admin-consent para Graph (D3 §12.6) — solo cuando el connect normal falló con
    /// AADSTS90094/consent_required. Un admin del tenant de Microsoft visita <c>Url</c> para otorgar
    /// consentimiento a nivel organización; la mayoría de los tenants nunca lo necesita.
    /// </summary>
    [HttpGet("accounts/admin-consent-url")]
    [HasPermission(ConnectorsPermissions.AccountsWrite)]
    public async Task<IActionResult> AdminConsentUrl(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<InitiateAdminConsentResult>(
            new InitiateAdminConsentCommand(tenantId, userId),
            ct
        );
        return Ok(result);
    }

    /// <summary>
    /// Reintenta el setup de watch/subscription de una cuenta (Fase 6) — vía manual tras
    /// TenantEmailAccount.Status == Error (renewal agotó reintentos, o el OAuth grant se revalidó
    /// fuera de banda). Mismo comando que el connect inicial (SetupWatchCommand): la invariante de
    /// TenantEmailAccount.Activate ya rechaza limpio si la cuenta no está en Draft/Connected/Error.
    /// </summary>
    [HttpPost("accounts/{id:guid}/reauth")]
    [HasPermission(ConnectorsPermissions.AccountsWrite)]
    public async Task<IActionResult> Reauth(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(new SetupWatchCommand(tenantId, id), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
