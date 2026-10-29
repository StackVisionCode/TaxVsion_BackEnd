using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record OpenPortalFormRequestCommand(ParticipantContext Context, Guid RequestId, DateTime NowUtc);

public static class OpenPortalFormRequestHandler
{
    public static async Task<Result<FormRequestRowVersionResponse>> Handle(
        OpenPortalFormRequestCommand command,
        IFormRequestRepository requests,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetForCommandAsync(command.Context.TenantId, command.RequestId, ct);
        if (found.IsFailure || !FormRequestApplicationSupport.Matches(command.Context, found.Value))
            return Result.Failure<FormRequestRowVersionResponse>(FormRequestErrors.NotFound);
        if (found.Value.MarkOpened(command.NowUtc))
        {
            audit.RecordParticipant(
                command.Context.TenantId,
                command.RequestId,
                "request.opened",
                command.Context.Actor
            );
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Result.Success(FormRequestRowVersionResponse.From(found.Value));
    }
}
