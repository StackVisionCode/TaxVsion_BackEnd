using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Api.Authorization;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Application.Email.Sending.Commands;
using TaxVision.Notification.Application.Email.Sending.Queries;
using TaxVision.Notification.Domain.Emailing.Sending;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Envío de correos (individual y por plantilla) e historial de mensajes salientes. El envío es
/// asíncrono: los endpoints devuelven 202 y el mensaje se entrega por evento durable fuera del request.
/// </summary>
[ApiController]
[Route("notifications/email")]
[Authorize]
public sealed class EmailSendController(IMessageBus bus) : ControllerBase
{
    public sealed record SendEmailRequest(
        string Subject,
        string HtmlBody,
        IReadOnlyList<EmailRecipientInput> Recipients,
        string? TextBody = null,
        EmailPriority Priority = EmailPriority.Normal,
        IReadOnlyList<Guid>? AttachmentFileIds = null
    );

    public sealed record SendTemplateRequest(
        Guid TemplateId,
        IReadOnlyList<EmailRecipientInput> Recipients,
        Dictionary<string, string?>? Variables = null,
        EmailPriority Priority = EmailPriority.Normal,
        IReadOnlyList<Guid>? AttachmentFileIds = null,
        bool ApplyLayout = true
    );

    [HttpPost("send")]
    [HasPermission(NotificationPermissions.EmailSend)]
    [ProducesResponseType<OutboundEmailResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Send([FromBody] SendEmailRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var command = new SendEmailCommand(
            tenantId,
            request.Subject,
            request.HtmlBody,
            request.TextBody,
            request.Priority,
            request.Recipients,
            request.AttachmentFileIds
        );
        var result = await bus.InvokeAsync<Result<OutboundEmailResponse>>(command, ct);
        return result.IsSuccess
            ? Accepted($"/notifications/email/messages/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("send-template")]
    [HasPermission(NotificationPermissions.EmailSend)]
    [ProducesResponseType<OutboundEmailResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SendTemplate([FromBody] SendTemplateRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var command = new SendTemplateEmailCommand(
            tenantId,
            request.TemplateId,
            request.Variables ?? new Dictionary<string, string?>(),
            request.Priority,
            request.Recipients,
            request.AttachmentFileIds,
            request.ApplyLayout
        );
        var result = await bus.InvokeAsync<Result<OutboundEmailResponse>>(command, ct);
        return result.IsSuccess
            ? Accepted($"/notifications/email/messages/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("messages")]
    [HasPermission(NotificationPermissions.EmailView)]
    [ProducesResponseType<PagedResult<OutboundEmailResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] EmailStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<OutboundEmailResponse>>>(
            new GetOutboundEmailsQuery(tenantId, status, page, size),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("messages/{id:guid}")]
    [HasPermission(NotificationPermissions.EmailView)]
    [ProducesResponseType<OutboundEmailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<OutboundEmailResponse>>(
            new GetOutboundEmailByIdQuery(id, tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
