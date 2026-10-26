using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record LinkFormRequestCustomerCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    Guid CustomerId,
    byte[] RowVersion,
    DateTime NowUtc
);

public static class LinkFormRequestCustomerHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        LinkFormRequestCustomerCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IClientFormsCustomerReader customers,
        IFormParticipantRepository participants,
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
        var linked = await LinkAsync(command, found.Value, requests, visibility, customers, participants, ct);
        if (linked.IsFailure)
            return Result.Failure<FormRequestResponse>(linked.Error);
        audit.Record(
            command.TenantId,
            found.Value.FormTemplateId,
            found.Value.Id,
            "customer_link.completed",
            command.ByUserId
        );
        await unitOfWork.SaveChangesAsync(ct);
        await FormCustomerLinker.RequestFileReassignmentAsync(found.Value, bus, correlation.CorrelationId);
        return Result.Success(FormRequestResponse.From(found.Value));
    }

    private static async Task<Result> LinkAsync(
        LinkFormRequestCustomerCommand command,
        FormRequest request,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IClientFormsCustomerReader customers,
        IFormParticipantRepository participants,
        CancellationToken ct
    )
    {
        var target = await ValidateTargetAsync(command, request, visibility, customers, ct);
        if (target.IsFailure)
            return Result.Failure(target.Error);
        var free = await EnsureNoConflictAsync(command, request, requests, ct);
        if (free.IsFailure)
            return free;
        var participant = await participants.GetAsync(command.TenantId, target.Value, ct);
        if (participant is null)
            return Result.Failure(FormRequestErrors.InvalidParticipant);
        var attached = request.TryAttachCustomer(command.CustomerId, false, null, command.NowUtc);
        return attached.IsFailure
            ? Result.Failure(attached.Error)
            : participant.LinkToCustomer(command.CustomerId, command.ByUserId, command.NowUtc);
    }

    private static async Task<Result<Guid>> ValidateTargetAsync(
        LinkFormRequestCustomerCommand command,
        FormRequest request,
        IFormRequestVisibilityFilter visibility,
        IClientFormsCustomerReader customers,
        CancellationToken ct
    )
    {
        if (!FormRequestApplicationSupport.RowVersionMatches(request, command.RowVersion))
            return Result.Failure<Guid>(FormRequestErrors.ConcurrencyConflict);
        if (request.ParticipantKind != FormParticipantKind.External || request.ParticipantId is not { } participantId)
            return Result.Failure<Guid>(FormRequestErrors.InvalidParticipant);
        var customer = await customers.GetAsync(command.TenantId, command.CustomerId, ct);
        if (customer is null)
            return Result.Failure<Guid>(FormRequestErrors.NotFound);
        if (!customer.IsActive)
            return Result.Failure<Guid>(FormRequestErrors.CustomerUnavailable);
        var visible = await visibility.CanViewCustomerAsync(
            command.TenantId,
            command.ByUserId,
            command.CanViewAll,
            command.CustomerId,
            ct
        );
        return visible ? Result.Success(participantId) : Result.Failure<Guid>(FormRequestErrors.NotFound);
    }

    private static async Task<Result> EnsureNoConflictAsync(
        LinkFormRequestCustomerCommand command,
        FormRequest request,
        IFormRequestRepository requests,
        CancellationToken ct
    )
    {
        if (request.CustomerLinkStatus == CustomerLinkStatus.Linked)
            return Result.Success();
        var conflict = await requests.FindOpenRequestIdAsync(
            command.TenantId,
            command.CustomerId,
            request.FormTemplateId,
            request.Id,
            ct
        );
        return conflict.HasValue ? Result.Failure(FormRequestErrors.CustomerHasOpenRequest) : Result.Success();
    }
}
