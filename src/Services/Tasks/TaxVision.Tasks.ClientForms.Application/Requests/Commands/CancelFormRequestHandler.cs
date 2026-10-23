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

public static class CancelFormRequestHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        CancelFormRequestCommand command,
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
            return Result.Failure<FormRequestResponse>(found.Error);
        var writable = ReviewSupport.EnsureWritable(found.Value, command.RowVersion, command.Note);
        if (writable.IsFailure)
            return Result.Failure<FormRequestResponse>(writable.Error);
        var cancelled = found.Value.Cancel(command.Note, command.ByUserId, command.NowUtc);
        if (cancelled.IsSuccess)
            await CloseAccessAndAnnounceAsync(found.Value, command, grants, sessions, bus, correlation, ct);
        return await ReviewSupport.Save(
            cancelled,
            found.Value,
            "request.cancelled",
            command.ByUserId,
            audit,
            unitOfWork,
            ct
        );
    }

    private static async Task CloseAccessAndAnnounceAsync(
        FormRequest request,
        CancelFormRequestCommand command,
        IFormAccessGrantRepository grants,
        IFormAccessSessionRepository sessions,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        await FormExternalAccessRevoker.RevokeAsync(request, command.NowUtc, grants, sessions, ct);
        await bus.PublishAsync(
            FormRequestIntegrationEventFactory.Cancelled(
                request,
                command.ByUserId,
                request.FirstOpenedAtUtc.HasValue,
                correlation.CorrelationId
            )
        );
    }
}
