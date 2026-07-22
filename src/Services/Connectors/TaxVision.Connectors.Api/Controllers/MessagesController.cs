using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Connectors.Api.Requests;
using TaxVision.Connectors.Application.Messages;
using TaxVision.Connectors.Application.Providers;
using Wolverine;

namespace TaxVision.Connectors.Api.Controllers;

/// <summary>
/// M2M interno — solo Correspondence (u otro microservicio backend), nunca un usuario final ni el
/// frontend (política "ServiceOnly", claim <c>actor_type=Service</c>). No debe exponerse en las
/// rutas públicas del Gateway. Body-fetch bajo demanda (Fase 8): nunca se llama desde el pipeline
/// de webhooks metadata-first (Fase 7).
/// </summary>
[ApiController]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
[Route("connectors/messages")]
public sealed class MessagesController(IMessageBus bus) : ControllerBase
{
    [HttpPost("{providerMessageId}/body")]
    public async Task<IActionResult> GetBody(
        string providerMessageId,
        [FromBody] GetMessageBodyRequest body,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<MessageBodyDto>>(
            new GetMessageBodyQuery(body.TenantId, body.AccountId, providerMessageId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{providerMessageId}/attachments/{attachmentId}")]
    public async Task<IActionResult> GetAttachment(
        string providerMessageId,
        string attachmentId,
        [FromBody] GetMessageBodyRequest body,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<MessageAttachmentDownload>>(
            new GetMessageAttachmentQuery(body.TenantId, body.AccountId, providerMessageId, attachmentId),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var download = result.Value;
        return File(download.Content, download.ContentType, download.Filename);
    }

    /// <summary>D3 §3.7 — el token nunca sale de Connectors: el caller (Postmaster) manda un DTO normalizado, nunca ve el access token de Gmail/Graph.</summary>
    [HttpPost("~/connectors/accounts/{accountId:guid}/send")]
    public async Task<IActionResult> Send(Guid accountId, [FromBody] SendMessageRequest body, CancellationToken ct)
    {
        List<OutboundAttachment> attachments;
        try
        {
            attachments = (body.Attachments ?? [])
                .Select(a => new OutboundAttachment(
                    a.Filename,
                    a.ContentType,
                    Convert.FromBase64String(a.ContentBase64)
                ))
                .ToList();
        }
        catch (FormatException)
        {
            return BadRequest(
                new Error(
                    "Connectors.InvalidAttachmentEncoding",
                    "One or more attachments have invalid base64 content."
                )
            );
        }

        var message = new OutboundMessage(
            body.Subject,
            body.Html,
            body.Text,
            body.To,
            body.Cc,
            body.Bcc,
            body.ReplyToDisplayAddress,
            body.InReplyToInternetMessageId,
            body.References,
            body.ReplyToProviderMessageId,
            attachments
        );

        var result = await bus.InvokeAsync<Result<SendMessageResult>>(
            new SendMessageCommand(body.TenantId, accountId, message),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
