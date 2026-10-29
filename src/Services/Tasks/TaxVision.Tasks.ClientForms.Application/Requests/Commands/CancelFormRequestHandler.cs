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
            await FormExternalAccessRevoker.RevokeAsync(found.Value, command.NowUtc, grants, sessions, ct);
        var saved = await ReviewSupport.Save(
            cancelled,
            found.Value,
            "request.cancelled",
            command.ByUserId,
            audit,
            unitOfWork,
            ct
        );
        // Guardar antes de publicar (T-8): un conflicto no anuncia una cancelación revertida.
        if (saved.IsSuccess)
            await AnnounceAsync(found.Value, command, bus, correlation);
        return saved;
    }

    private static async Task AnnounceAsync(
        FormRequest request,
        CancelFormRequestCommand command,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
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
