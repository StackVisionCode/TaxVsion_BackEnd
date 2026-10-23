using BuildingBlocks.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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

    public void RecordStaffOutcome(
        Guid tenantId,
        Guid? templateId,
        Guid? requestId,
        string action,
        string outcome,
        Guid actorUserId,
        string? detailsJson = null,
        FormAuditRequestContext? requestContext = null
    )
    {
        var http = httpContextAccessor.HttpContext;
        context.FormAuditEntries.Add(
            FormAuditEntry.Create(
                tenantId,
                templateId,
                requestId,
                action,
                outcome,
                actorUserId,
                "Staff",
                null,
                null,
                Limit(requestContext?.IpAddress ?? http?.Connection.RemoteIpAddress?.ToString(), 64),
                Limit(requestContext?.UserAgent ?? http?.Request.Headers.UserAgent.ToString(), 512),
                Limit(requestContext?.CorrelationId ?? correlation.CorrelationId, 64) ?? string.Empty,
                Limit(detailsJson, 2000),
                DateTime.UtcNow
            )
        );
    }

    public async Task RecordAnswersViewedAsync(
        Guid tenantId,
        Guid requestId,
        int revisionNumber,
        Guid actorUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        var details = $"{{\"revisionNumber\":{revisionNumber}}}";
        var cutoff = nowUtc.AddMinutes(-30);
        var exists = await context
            .FormAuditEntries.IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                item =>
                    item.TenantId == tenantId
                    && item.FormRequestId == requestId
                    && item.ActorUserId == actorUserId
                    && item.Action == "answers.viewed"
                    && item.DetailsJson == details
                    && item.OccurredAtUtc >= cutoff,
                ct
            );
        if (!exists)
            Record(tenantId, null, requestId, "answers.viewed", actorUserId, details);
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
