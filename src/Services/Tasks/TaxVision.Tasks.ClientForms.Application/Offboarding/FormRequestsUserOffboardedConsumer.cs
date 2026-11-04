using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Offboarding;

/// <summary>
/// Al retirar a un empleado, sus requests ABIERTAS (Pending/Submitted/Returned) cuyo responsable efectivo
/// es él (<c>ResponsibleUserId</c>, o <c>RequestedByUserId</c> si no hay responsable) pasan al sucesor; sin
/// sucesor elegible, a quien ejecutó el retiro (el CHECK de BD no admite responsable nulo en externas y el
/// admin que retira es el único staff activo que el evento garantiza). Misma regla que la reasignación manual:
/// activo + <c>tasks.form_requests.review</c>. Si ninguno es elegible no se toca nada y queda el aviso.
/// <c>RequestedByUserId</c> (procedencia) nunca cambia. Idempotente: lo reasignado sale del filtro.
/// </summary>
public static class FormRequestsUserOffboardedConsumer
{
    private const int BatchSize = 200;

    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        IFormRequestRepository requests,
        IFormResponsibleUserValidator responsibleUsers,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<FormRequest> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(OffboardingSupport.CorrelationIdOf(msg));
        var target = await ResolveTargetAsync(msg, responsibleUsers, ct);
        if (target is null)
        {
            await FlagUnassignableAsync(msg, requests, audit, unitOfWork, logger, ct);
            return;
        }

        var moved = await ReassignAsync(msg, target.Value, requests, audit, unitOfWork, ct);
        if (moved > 0)
            logger.LogInformation(
                "Offboarded user {UserId}: {Count} open form request(s) reassigned to {TargetUserId} in tenant {TenantId}.",
                msg.UserId,
                moved,
                target,
                msg.TenantId
            );
    }

    // Sucesor primero; si no es elegible, quien ejecutó el retiro.
    private static async Task<Guid?> ResolveTargetAsync(
        UserOffboardedIntegrationEvent msg,
        IFormResponsibleUserValidator responsibleUsers,
        CancellationToken ct
    )
    {
        foreach (var candidate in new[] { OffboardingSupport.SuccessorOf(msg), OffboardingSupport.OffboardedByOf(msg) })
        {
            if (
                candidate is { } userId
                && await FormRequestResponsibleRules.IsEligibleAsync(msg.TenantId, userId, responsibleUsers, ct)
            )
                return userId;
        }

        return null;
    }

    // Siempre la "primera página": cada request reasignada sale del filtro. Corta si un lote no cambia nada.
    private static async Task<int> ReassignAsync(
        UserOffboardedIntegrationEvent msg,
        Guid target,
        IFormRequestRepository requests,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var total = 0;
        while (true)
        {
            var batch = await requests.ListOpenByEffectiveResponsibleAsync(msg.TenantId, msg.UserId, BatchSize, ct);
            var changed = 0;
            foreach (var request in batch)
            {
                if (request.AssignResponsible(target, msg.OccurredOn).IsFailure)
                    continue;
                Record(audit, msg, request, FormAuditAction.RequestResponsibleChanged, "success", target);
                changed++;
            }

            await unitOfWork.SaveChangesAsync(ct);
            total += changed;
            if (changed == 0)
                return total;
        }
    }

    private static async Task FlagUnassignableAsync(
        UserOffboardedIntegrationEvent msg,
        IFormRequestRepository requests,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        var stranded = await requests.ListOpenByEffectiveResponsibleAsync(msg.TenantId, msg.UserId, BatchSize, ct);
        if (stranded.Count == 0)
            return;
        foreach (var request in stranded)
            Record(audit, msg, request, FormAuditAction.RequestResponsibleUnavailable, "warning", null);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning(
            "Offboarded user {UserId}: no eligible responsible for {Count} open form request(s) in tenant {TenantId}.",
            msg.UserId,
            stranded.Count,
            msg.TenantId
        );
    }

    private static void Record(
        IFormAuditLog audit,
        UserOffboardedIntegrationEvent msg,
        FormRequest request,
        string action,
        string outcome,
        Guid? newResponsibleUserId
    ) =>
        audit.RecordSystemOutcome(
            request.TenantId,
            request.FormTemplateId,
            request.Id,
            action,
            outcome,
            JsonSerializer.Serialize(
                new
                {
                    offboardedUserId = msg.UserId,
                    newResponsibleUserId,
                    msg.OffboardedByUserId,
                }
            )
        );
}
