using BuildingBlocks.Common;
using Microsoft.AspNetCore.Http;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Audit;

internal sealed class FormAuditLog(
    TasksDbContext context,
    ICorrelationContext correlation,
    IHttpContextAccessor httpContextAccessor
) : IFormAuditLog
{
    public void Record(
        Guid tenantId,
        Guid? templateId,
        Guid? requestId,
        string action,
        Guid? actorUserId,
        string? detailsJson = null
    )
    {
        var http = httpContextAccessor.HttpContext;
        var ip = http?.Connection.RemoteIpAddress?.ToString();
        var userAgent = Limit(http?.Request.Headers.UserAgent.ToString(), 512);
        context.FormAuditEntries.Add(
            FormAuditEntry.Create(
                tenantId,
                templateId,
                requestId,
                action,
                "success",
                actorUserId,
                actorUserId.HasValue ? "Staff" : "System",
                null,
                null,
                ip,
                userAgent,
                Limit(correlation.CorrelationId, 64) ?? string.Empty,
                Limit(detailsJson, 2000),
                DateTime.UtcNow
            )
        );
    }

    public void RecordParticipant(
        Guid tenantId,
        Guid requestId,
        string action,
        FormActor actor,
        string? detailsJson = null
    )
    {
        var http = httpContextAccessor.HttpContext;
        context.FormAuditEntries.Add(
            FormAuditEntry.Create(
                tenantId,
                null,
                requestId,
                action,
                "success",
                actor.PortalUserId,
                actor.Kind == FormActorKind.PortalUser ? "CustomerPortal" : "ExternalParticipant",
                actor.ParticipantId,
                actor.SessionId,
                http?.Connection.RemoteIpAddress?.ToString(),
                Limit(http?.Request.Headers.UserAgent.ToString(), 512),
                Limit(correlation.CorrelationId, 64) ?? string.Empty,
                Limit(detailsJson, 2000),
                DateTime.UtcNow
            )
        );
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}
