using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Offboarding;

/// <summary>
/// Al retirar a un empleado, sus enlaces públicos de captación (no revocados) pasan al sucesor si es
/// elegible (activo y con los permisos de las acciones de la versión fijada, igual que al crear el enlace).
/// Sin sucesor elegible el enlace se PAUSA y queda en la auditoría para que un admin lo reasigne; reanudarlo
/// exige antes un responsable activo. Idempotente: lo ya reasignado sale del filtro y lo pausado no se repite.
/// </summary>
public static class FormIntakeLinksUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        IFormIntakeLinkRepository links,
        IFormTemplateVersionRepository versions,
        IFormResponsibleUserValidator responsibleUsers,
        IFormActionAuthorizer authorizer,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<FormIntakeLink> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(OffboardingSupport.CorrelationIdOf(msg));
        var affected = await links.ListNotRevokedByResponsibleAsync(msg.TenantId, msg.UserId, ct);
        if (affected.Count == 0)
            return;

        var sources = new Sources(versions, responsibleUsers, authorizer, audit);
        var reassigned = 0;
        foreach (var link in affected)
            reassigned += await HandOverAsync(msg, link, sources, ct) ? 1 : 0;

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Offboarded user {UserId}: {Reassigned} intake link(s) reassigned, {Paused} left paused in tenant {TenantId}.",
            msg.UserId,
            reassigned,
            affected.Count - reassigned,
            msg.TenantId
        );
    }

    private static async Task<bool> HandOverAsync(
        UserOffboardedIntegrationEvent msg,
        FormIntakeLink link,
        Sources sources,
        CancellationToken ct
    )
    {
        var successor = OffboardingSupport.SuccessorOf(msg);
        if (successor is { } candidate && await IsEligibleAsync(link, candidate, sources, ct))
        {
            link.ReassignResponsible(candidate);
            Record(sources.Audit, msg, link, FormAuditAction.IntakeLinkResponsibleChanged, "success", candidate);
            return true;
        }

        if (link.Pause().IsSuccess)
            Record(sources.Audit, msg, link, FormAuditAction.IntakeLinkPausedResponsibleOffboarded, "warning", null);
        return false;
    }

    private static async Task<bool> IsEligibleAsync(
        FormIntakeLink link,
        Guid candidate,
        Sources sources,
        CancellationToken ct
    )
    {
        var version = await sources.Versions.GetByIdAsync(link.TenantId, link.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return false;
        var eligible = await FormIntakeLinkResponsibleRules.EnsureEligibleAsync(
            link.TenantId,
            candidate,
            version.Value,
            sources.ResponsibleUsers,
            sources.Authorizer,
            ct
        );
        return eligible.IsSuccess;
    }

    private static void Record(
        IFormAuditLog audit,
        UserOffboardedIntegrationEvent msg,
        FormIntakeLink link,
        string action,
        string outcome,
        Guid? newResponsibleUserId
    ) =>
        audit.RecordSystemOutcome(
            link.TenantId,
            link.FormTemplateId,
            null,
            action,
            outcome,
            JsonSerializer.Serialize(
                new
                {
                    linkId = link.Id,
                    offboardedUserId = msg.UserId,
                    newResponsibleUserId,
                    msg.OffboardedByUserId,
                }
            )
        );

    private sealed record Sources(
        IFormTemplateVersionRepository Versions,
        IFormResponsibleUserValidator ResponsibleUsers,
        IFormActionAuthorizer Authorizer,
        IFormAuditLog Audit
    );
}
