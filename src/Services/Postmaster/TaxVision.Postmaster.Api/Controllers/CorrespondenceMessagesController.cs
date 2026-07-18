using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Postmaster.Api.Common;
using TaxVision.Postmaster.Api.Requests;
using TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;
using TaxVision.Postmaster.Domain.Sending;
using Wolverine;

namespace TaxVision.Postmaster.Api.Controllers;

/// <summary>
/// M2M interno — solo Correspondence, nunca un usuario final (política "ServiceOnly", D3 Compose
/// §14/§15). No debe exponerse en las rutas públicas del Gateway.
/// </summary>
[ApiController]
[Authorize(Policy = "ServiceOnly")]
[Route("postmaster/correspondence-messages")]
public sealed class CorrespondenceMessagesController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendCorrespondenceMessageRequest body, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != body.TenantId)
            return Forbid();

        var attachmentsResult = BuildAttachments(body.Attachments);
        if (attachmentsResult.IsFailure)
            return StatusCode(attachmentsResult.Error.ToHttpStatusCode(), attachmentsResult.Error);

        var command = new SendCorrespondenceMessageCommand(
            body.TenantId,
            body.CorrespondenceDraftId,
            body.AccountId,
            body.Subject,
            body.Html,
            body.Text,
            body.To,
            body.Cc,
            body.Bcc,
            attachmentsResult.Value,
            body.ReplyContext?.InReplyToInternetMessageId,
            body.ReplyContext?.References,
            body.ReplyContext?.ReplyToProviderMessageId
        );

        var result = await bus.InvokeAsync<Result<SendCorrespondenceMessageResult>>(command, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static Result<IReadOnlyList<OutboundAttachmentRef>> BuildAttachments(
        IReadOnlyList<CorrespondenceAttachmentRequest>? attachments
    )
    {
        if (attachments is null or { Count: 0 })
            return Result.Success<IReadOnlyList<OutboundAttachmentRef>>([]);

        var built = new List<OutboundAttachmentRef>(attachments.Count);
        foreach (var attachment in attachments)
        {
            var createResult = OutboundAttachmentRef.Create(
                attachment.FileId,
                attachment.Filename,
                attachment.ContentType,
                attachment.SizeBytes
            );
            if (createResult.IsFailure)
                return Result.Failure<IReadOnlyList<OutboundAttachmentRef>>(createResult.Error);

            built.Add(createResult.Value);
        }

        return Result.Success<IReadOnlyList<OutboundAttachmentRef>>(built);
    }
}
