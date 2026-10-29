using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public static class DetachPortalFormAttachmentHandler
{
    public static async Task<Result<FormRequestRowVersionResponse>> Handle(
        DetachPortalFormAttachmentCommand command,
        IFormRequestRepository requests,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.Context.TenantId, command.RequestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure<FormRequestRowVersionResponse>(FormRequestErrors.NotFound);
        var detached = found.Value.DetachAttachment(command.AttachmentId, command.NowUtc);
        if (detached.IsFailure)
            return Result.Failure<FormRequestRowVersionResponse>(detached.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormRequestRowVersionResponse.From(found.Value));
    }
}
