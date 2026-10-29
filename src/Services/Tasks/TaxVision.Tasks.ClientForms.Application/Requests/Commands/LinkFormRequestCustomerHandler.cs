using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
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
        var linked = await LinkAsync(command, found.Value, requests, visibility, customers, participants, audit, ct);
        if (linked.IsFailure)
            return Result.Failure<FormRequestResponse>(linked.Error);
        var outgoing = await AfterLinkAsync(command, found.Value, linked.Value, requests, audit, correlation, ct);
        // Guardar antes de publicar (T-8).
        await unitOfWork.SaveChangesAsync(ct);
        await outgoing.PublishAsync(bus);
        return Result.Success(FormRequestResponse.From(found.Value));
    }

    // Audit, reasignación de archivos y, si el participante pasó a ser este cliente, sus otras requests.
    private static async Task<DeferredMessages> AfterLinkAsync(
        LinkFormRequestCustomerCommand command,
        FormRequest request,
        bool participantLinked,
        IFormRequestRepository requests,
        IFormAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        audit.Record(command.TenantId, request.FormTemplateId, request.Id, "customer_link.completed", command.ByUserId);
        var outgoing = new DeferredMessages();
        FormCustomerLinker.RequestFileReassignment(request, outgoing, correlation.CorrelationId);
        if (participantLinked)
            await FormCustomerLinker.PropagateToSiblingsAsync(
                request,
                command.CustomerId,
                requests,
                audit,
                outgoing,
                correlation.CorrelationId,
                command.NowUtc,
                ct
            );
        return outgoing;
    }

    // Devuelve true si el participante quedó vinculado a este cliente. Si ya era otro cliente, la request
    // se vincula igual (decisión del staff), el participante conserva el original y queda el aviso.
    private static async Task<Result<bool>> LinkAsync(
        LinkFormRequestCustomerCommand command,
        FormRequest request,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IClientFormsCustomerReader customers,
        IFormParticipantRepository participants,
        IFormAuditLog audit,
        CancellationToken ct
    )
    {
        var target = await ValidateTargetAsync(command, request, visibility, customers, ct);
        if (target.IsFailure)
            return Result.Failure<bool>(target.Error);
        var free = await EnsureNoConflictAsync(command, request, requests, ct);
        if (free.IsFailure)
            return Result.Failure<bool>(free.Error);
        var participant = await participants.GetAsync(command.TenantId, target.Value, ct);
        // Se valida antes de tocar la request: un fallo no deja un vínculo a medias.
        if (participant is null || participant.Status == FormParticipantStatus.Unverified)
            return Result.Failure<bool>(FormRequestErrors.InvalidParticipant);
        var attached = request.TryAttachCustomer(command.CustomerId, false, null, command.NowUtc);
        if (attached.IsFailure)
            return Result.Failure<bool>(attached.Error);
        var linked = participant.LinkToCustomer(command.CustomerId, command.ByUserId, command.NowUtc);
        if (linked.IsSuccess)
            return Result.Success(true);
        FormCustomerLinker.RecordConflict(request, participant.LinkedCustomerId, command.CustomerId, audit);
        return Result.Success(false);
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
