using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

/// <summary>
/// Crear el cliente y vincular la request son dos pasos con resultados propios: un conflicto de
/// vinculación queda registrado como Blocked y nunca lanza, para no perder el cliente ya creado.
/// El participante se comparte por email: cuando pasa a ser un cliente, sus otras requests abiertas
/// heredan ese vínculo; si ya era otro cliente, se conserva el original y queda un aviso en la actividad.
/// </summary>
public static class FormCustomerLinker
{
    public const string LinkConflictAction = "participant.link_conflict";
    public const string LinkInheritedAction = "customer_link.inherited";

    public static async Task LinkCreatedCustomerAsync(
        FormRequest request,
        Guid customerId,
        IFormRequestRepository requests,
        IFormParticipantRepository participants,
        IFormAuditLog audit,
        DeferredMessages outgoing,
        string correlationId,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var participantLinked = await LinkParticipantAsync(
            request,
            customerId,
            participants,
            audit,
            logger,
            nowUtc,
            ct
        );
        await AttachAsync(request, customerId, requests, audit, outgoing, correlationId, logger, nowUtc, ct);
        if (participantLinked)
            await PropagateToSiblingsAsync(request, customerId, requests, audit, outgoing, correlationId, nowUtc, ct);
    }

    // Se publica después de guardar el vínculo (quien llama hace PublishAsync tras el SaveChanges).
    public static void RequestFileReassignment(FormRequest request, DeferredMessages outgoing, string correlationId)
    {
        if (request.FilesPendingOwnerReassignment().Count > 0)
            outgoing.Add(new ReassignParticipantFilesRequested(request.TenantId, request.Id, correlationId));
    }

    // El participante es ese cliente aunque la request todavía no pueda vincularse. Devuelve true si el
    // participante queda vinculado a ESTE cliente; si ya era otro, conserva el original y avisa.
    public static async Task<bool> LinkParticipantAsync(
        FormRequest request,
        Guid customerId,
        IFormParticipantRepository participants,
        IFormAuditLog audit,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (request.ParticipantId is not { } participantId || request.ResponsibleUserId is not { } responsible)
            return false;
        var participant = await participants.GetAsync(request.TenantId, participantId, ct);
        if (participant is null)
            return false;
        var linked = participant.LinkToCustomer(customerId, responsible, nowUtc);
        if (linked.IsSuccess)
            return true;
        if (linked.Error == FormParticipantErrors.AlreadyLinked)
            RecordConflict(request, participant.LinkedCustomerId, customerId, audit);
        logger.LogWarning(
            "Form participant {ParticipantId} could not be linked to customer {CustomerId}: {Code}.",
            participantId,
            customerId,
            linked.Error.Code
        );
        return false;
    }

    // Las otras requests abiertas del participante pasan al mismo cliente; una que choca queda Blocked.
    public static async Task PropagateToSiblingsAsync(
        FormRequest origin,
        Guid customerId,
        IFormRequestRepository requests,
        IFormAuditLog audit,
        DeferredMessages outgoing,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (origin.ParticipantId is not { } participantId)
            return;
        var siblings = await requests.ListOpenByParticipantAsync(origin.TenantId, participantId, ct);
        foreach (var sibling in siblings)
        {
            if (sibling.Id == origin.Id || sibling.CustomerLinkStatus != CustomerLinkStatus.None)
                continue;
            var conflict = await requests.FindOpenRequestIdAsync(
                sibling.TenantId,
                customerId,
                sibling.FormTemplateId,
                sibling.Id,
                ct
            );
            var attached = sibling.TryAttachCustomer(customerId, conflict.HasValue, conflict, nowUtc);
            if (attached.IsFailure)
                continue;
            AfterAttach(sibling, attached.Value, outgoing, correlationId);
            audit.RecordSystemOutcome(
                sibling.TenantId,
                sibling.FormTemplateId,
                sibling.Id,
                LinkInheritedAction,
                attached.Value == CustomerLinkResult.Blocked ? "blocked" : "success",
                JsonSerializer.Serialize(new { customerId, fromRequestId = origin.Id })
            );
        }
    }

    // La request ya vinculada a otro cliente conserva su vínculo: queda el aviso, nunca un fallo mudo.
    private static async Task AttachAsync(
        FormRequest request,
        Guid customerId,
        IFormRequestRepository requests,
        IFormAuditLog audit,
        DeferredMessages outgoing,
        string correlationId,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
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
        if (attached.IsSuccess)
        {
            AfterAttach(request, attached.Value, outgoing, correlationId);
            return;
        }
        if (attached.Error == FormRequestErrors.AlreadyLinked)
            RecordConflict(request, request.CustomerId, customerId, audit);
        logger.LogWarning(
            "Form request {FormRequestId} could not be linked to the created customer: {Code}.",
            request.Id,
            attached.Error.Code
        );
    }

    private static void AfterAttach(
        FormRequest request,
        CustomerLinkResult result,
        DeferredMessages outgoing,
        string correlationId
    )
    {
        if (result == CustomerLinkResult.Linked)
            RequestFileReassignment(request, outgoing, correlationId);
        else if (result == CustomerLinkResult.Blocked)
            outgoing.Add(
                CustomerCreationCommandFactory.Failed(
                    request,
                    FormActionNames.CreateCustomer,
                    FormActionNames.LinkBlockedCode,
                    correlationId
                )
            );
    }

    public static void RecordConflict(
        FormRequest request,
        Guid? keptCustomerId,
        Guid attemptedCustomerId,
        IFormAuditLog audit
    ) =>
        audit.RecordSystemOutcome(
            request.TenantId,
            request.FormTemplateId,
            request.Id,
            LinkConflictAction,
            "warning",
            JsonSerializer.Serialize(new { keptCustomerId, attemptedCustomerId })
        );

    private static bool IsOpen(FormRequest request) =>
        request.Status is FormRequestStatus.Pending or FormRequestStatus.Submitted or FormRequestStatus.Returned;
}
