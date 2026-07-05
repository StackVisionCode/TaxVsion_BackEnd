using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Api.Authorization;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Application.Email.Accounts;
using TaxVision.Notification.Application.Email.Accounts.Commands;
using TaxVision.Notification.Application.Email.Accounts.Queries;
using TaxVision.Notification.Domain.Emailing.Accounts;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Conexión y sincronización de cuentas de correo externas (Gmail API, Microsoft Graph, IMAP). Cada
/// tenant solo ve sus propias cuentas. Los tokens/credenciales nunca se exponen. La sincronización
/// corre fuera del request (worker/cola).
/// </summary>
[ApiController]
[Route("notifications/email/accounts")]
[Authorize]
public sealed class EmailAccountsController(IMessageBus bus) : ControllerBase
{
    public sealed record ConnectRequest(
        EmailExternalProvider Provider,
        string EmailAddress,
        string? DisplayName = null,
        string? AccessToken = null,
        string? RefreshToken = null,
        DateTime? TokenExpiresAtUtc = null,
        string? ExternalAccountId = null,
        string? ImapHost = null,
        int? ImapPort = null,
        string? ImapUsername = null,
        string? ImapPassword = null,
        bool ImapUseSsl = true
    );

    [HttpPost("connect")]
    [HasPermission(NotificationPermissions.AccountManage)]
    [ProducesResponseType<EmailAccountResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Connect([FromBody] ConnectRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var command = new ConnectEmailAccountCommand(
            tenantId,
            userId,
            request.Provider,
            request.EmailAddress,
            request.DisplayName,
            request.AccessToken,
            request.RefreshToken,
            request.TokenExpiresAtUtc,
            request.ExternalAccountId,
            request.ImapHost,
            request.ImapPort,
            request.ImapUsername,
            request.ImapPassword,
            request.ImapUseSsl
        );
        var result = await bus.InvokeAsync<Result<EmailAccountResponse>>(command, ct);
        return result.IsSuccess
            ? Created($"/notifications/email/accounts/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(NotificationPermissions.AccountView)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<IReadOnlyList<EmailAccountResponse>>>(new GetEmailAccountsQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(NotificationPermissions.AccountView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<EmailAccountResponse>>(new GetEmailAccountByIdQuery(id, tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/disconnect")]
    [HasPermission(NotificationPermissions.AccountManage)]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result>(new DisconnectEmailAccountCommand(id, tenantId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/sync")]
    [HasPermission(NotificationPermissions.AccountManage)]
    public async Task<IActionResult> Sync(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result>(new RequestAccountSyncCommand(id, tenantId, Full: false), ct);
        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/full-sync")]
    [HasPermission(NotificationPermissions.AccountManage)]
    public async Task<IActionResult> FullSync(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result>(new RequestAccountSyncCommand(id, tenantId, Full: true), ct);
        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/folders")]
    [HasPermission(NotificationPermissions.AccountView)]
    public async Task<IActionResult> Folders(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<IReadOnlyList<EmailFolderResponse>>>(new GetAccountFoldersQuery(id, tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/messages")]
    [HasPermission(NotificationPermissions.AccountView)]
    public async Task<IActionResult> Messages(
        Guid id,
        [FromQuery] Guid? folderId = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<PagedResult<EmailMessageSummaryResponse>>>(
            new GetAccountMessagesQuery(id, tenantId, folderId, page, size),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/messages/{messageId:guid}")]
    [HasPermission(NotificationPermissions.AccountView)]
    public async Task<IActionResult> Message(Guid id, Guid messageId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<EmailMessageDetailResponse>>(new GetAccountMessageQuery(id, tenantId, messageId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/threads/{threadId}")]
    [HasPermission(NotificationPermissions.AccountView)]
    public async Task<IActionResult> Thread(Guid id, string threadId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<IReadOnlyList<EmailMessageDetailResponse>>>(
            new GetAccountThreadQuery(id, tenantId, threadId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/sync-logs")]
    [HasPermission(NotificationPermissions.AccountView)]
    public async Task<IActionResult> SyncLogs(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<IReadOnlyList<EmailSyncLogResponse>>>(new GetAccountSyncLogsQuery(id, tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
