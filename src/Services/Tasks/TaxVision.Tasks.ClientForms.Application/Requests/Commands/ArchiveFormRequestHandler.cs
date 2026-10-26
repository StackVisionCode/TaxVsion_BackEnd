using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record ArchiveFormRequestCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    string? Note,
    byte[] RowVersion,
    DateTime NowUtc
);

public static class ArchiveFormRequestHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        ArchiveFormRequestCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormAccessGrantRepository grants,
        IFormAccessSessionRepository sessions,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var prepared = await PrepareAsync(command, requests, visibility, grants, sessions, audit, ct);
        if (prepared.IsFailure)
            return Result.Failure<FormRequestResponse>(prepared.Error);
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            FormRequestIntegrationEventFactory.Cancelled(
                prepared.Value,
                command.ByUserId,
                false,
                correlation.CorrelationId
            )
        );
        return Result.Success(FormRequestResponse.From(prepared.Value));
    }

    private static async Task<Result<FormRequest>> PrepareAsync(
        ArchiveFormRequestCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormAccessGrantRepository grants,
        IFormAccessSessionRepository sessions,
        IFormAuditLog audit,
        CancellationToken ct
    )
    {
        var found = await ReviewSupport.LoadVisible(
            command.TenantId,
            command.ByUserId,
            command.CanViewAll,
            command.RequestId,
            requests,
            visibility,
            ct
        );
        if (found.IsFailure)
            return found;
        var writable = ReviewSupport.EnsureWritable(found.Value, command.RowVersion, command.Note);
        if (writable.IsFailure)
            return Result.Failure<FormRequest>(writable.Error);
        var archived = found.Value.Archive(command.Note, command.ByUserId, command.NowUtc);
        if (archived.IsFailure)
            return Result.Failure<FormRequest>(archived.Error);
        await FormExternalAccessRevoker.RevokeAsync(found.Value, command.NowUtc, grants, sessions, ct);
        audit.Record(
            command.TenantId,
            found.Value.FormTemplateId,
            found.Value.Id,
            "request.archived",
            command.ByUserId
        );
        return found;
    }
}
