using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class CompleteExternalFormAttachmentUploadHandler
{
    public static async Task<Result> Handle(
        CompleteExternalFormAttachmentUploadCommand command,
        IFormRequestRepository requests,
        IFormExternalUploadClient uploads,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(
            command.Context.TenantId,
            command.Context.BoundRequestId ?? Guid.Empty,
            ct
        );
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure(FormRequestErrors.NotFound);
        var attachment = found.Value.Submission?.Attachments.SingleOrDefault(item =>
            item.FileId == command.FileId && item.Status == FormAttachmentStatus.Pending
        );
        return attachment is null
            ? Result.Failure(FormRequestErrors.AttachmentNotFound)
            : await uploads.CompleteAsync(command.Context.TenantId, command.FileId, ct);
    }
}
