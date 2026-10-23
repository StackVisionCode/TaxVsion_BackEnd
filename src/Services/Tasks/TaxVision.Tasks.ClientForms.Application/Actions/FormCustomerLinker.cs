using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

/// <summary>
/// Crear el cliente y vincular la request son dos pasos con resultados propios: un conflicto de
/// vinculación queda registrado como Blocked y nunca lanza, para no perder el cliente ya creado.
/// </summary>
public static class FormCustomerLinker
{
    public static async Task LinkCreatedCustomerAsync(
        FormRequest request,
        Guid customerId,
        IFormRequestRepository requests,
        IFormParticipantRepository participants,
        IMessageBus bus,
        string correlationId,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        await LinkParticipantAsync(request, customerId, participants, logger, nowUtc, ct);
        Guid? conflict = IsOpen(request)
            ? await requests.FindOpenRequestIdAsync(
                request.TenantId,
                customerId,
                request.FormTemplateId,
                request.Id,
                ct
            )
            : null;
        var attached = request.TryAttachCustomer(customerId, conflict.HasValue, conflict, nowUtc);
        if (attached.IsFailure)
        {
            logger.LogWarning(
                "Form request {FormRequestId} could not be linked to the created customer: {Code}.",
                request.Id,
                attached.Error.Code
            );
            return;
        }
        if (attached.Value == CustomerLinkResult.Linked)
            await RequestFileReassignmentAsync(request, bus, correlationId);
        else if (attached.Value == CustomerLinkResult.Blocked)
            await bus.PublishAsync(
                CustomerCreationCommandFactory.Failed(
                    request,
                    FormActionNames.CreateCustomer,
                    FormActionNames.LinkBlockedCode,
                    correlationId
                )
            );
    }

    public static async Task RequestFileReassignmentAsync(FormRequest request, IMessageBus bus, string correlationId)
    {
        if (request.FilesPendingOwnerReassignment().Count > 0)
            await bus.PublishAsync(new ReassignParticipantFilesRequested(request.TenantId, request.Id, correlationId));
    }

    // El participante es ese cliente aunque la request todavía no pueda vincularse.
    public static async Task LinkParticipantAsync(
        FormRequest request,
        Guid customerId,
        IFormParticipantRepository participants,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (request.ParticipantId is not { } participantId || request.ResponsibleUserId is not { } responsible)
            return;
        var participant = await participants.GetAsync(request.TenantId, participantId, ct);
        var linked = participant?.LinkToCustomer(customerId, responsible, nowUtc);
        if (linked is { IsFailure: true })
            logger.LogWarning(
                "Form participant {ParticipantId} could not be linked to customer {CustomerId}: {Code}.",
                participantId,
                customerId,
                linked.Error.Code
            );
    }

    private static bool IsOpen(FormRequest request) =>
        request.Status is FormRequestStatus.Pending or FormRequestStatus.Submitted or FormRequestStatus.Returned;
}
