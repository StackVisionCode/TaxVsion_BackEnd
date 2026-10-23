using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Commands;

// Reusa el cliente guardado en el bloqueo: nunca vuelve a pedirle nada a Customer.
public static class RetryFormCustomerLinkHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        RetryFormCustomerLinkCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormParticipantRepository participants,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<RetryFormCustomerLinkCommand> logger,
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
        if (found.IsFailure || found.Value.CustomerLinkStatus == CustomerLinkStatus.Linked)
            return found.IsFailure
                ? Result.Failure<FormRequestResponse>(found.Error)
                : Result.Success(FormRequestResponse.From(found.Value));
        var linked = await RelinkAsync(command, found.Value, requests, visibility, ct);
        if (linked.IsFailure)
            return Result.Failure<FormRequestResponse>(linked.Error);
        await FinishAsync(command, found.Value, linked.Value, participants, audit, bus, correlation, logger, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormRequestResponse.From(found.Value));
    }

    // El participante ya es ese cliente; los archivos pasan a él fuera de esta transacción.
    private static async Task FinishAsync(
        RetryFormCustomerLinkCommand command,
        FormRequest request,
        Guid customerId,
        IFormParticipantRepository participants,
        IFormAuditLog audit,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        await FormCustomerLinker.LinkParticipantAsync(request, customerId, participants, logger, command.NowUtc, ct);
        await FormCustomerLinker.RequestFileReassignmentAsync(request, bus, correlation.CorrelationId);
        audit.Record(command.TenantId, request.FormTemplateId, request.Id, "customer_link.retried", command.ByUserId);
    }

    // Vincular exige ver al cliente destino. Si el conflicto sigue, no se toca nada y se responde 409.
    private static async Task<Result<Guid>> RelinkAsync(
        RetryFormCustomerLinkCommand command,
        FormRequest request,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        CancellationToken ct
    )
    {
        if (
            request.CustomerLinkStatus != CustomerLinkStatus.Blocked
            || request.LinkBlockedCustomerId is not { } customerId
        )
            return Result.Failure<Guid>(FormRequestErrors.LinkNotBlocked);
        if (
            !await visibility.CanViewCustomerAsync(
                command.TenantId,
                command.ByUserId,
                command.CanViewAll,
                customerId,
                ct
            )
        )
            return Result.Failure<Guid>(FormRequestErrors.NotFound);
        var conflict = await requests.FindOpenRequestIdAsync(
            request.TenantId,
            customerId,
            request.FormTemplateId,
            request.Id,
            ct
        );
        if (conflict.HasValue)
            return Result.Failure<Guid>(FormRequestErrors.CustomerHasOpenRequest);
        var retried = request.RetryCustomerLink(false, null, command.NowUtc);
        return retried.IsFailure ? Result.Failure<Guid>(retried.Error) : Result.Success(customerId);
    }
}
