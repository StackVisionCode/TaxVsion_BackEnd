using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Messages.Queries;
using TaxVision.Sms.Application.OptOut.Queries;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;

namespace TaxVision.Sms.Infrastructure.Persistence;

/// <summary>
/// Read model del CRM. Corre en el scope de un handler de Wolverine (bus.InvokeAsync), distinto al de
/// la request HTTP que pobló ITenantContext, así que el HasQueryFilter ambiental ve Guid.Empty aquí —
/// por eso cada query usa IgnoreQueryFilters() + filtro EXPLÍCITO por tenantId (mismo criterio que
/// CustomerReadService). Solo lectura, AsNoTracking.
/// </summary>
public sealed class SmsReadService(SmsDbContext db) : ISmsReadService
{
    // Visibilidad por asignación (P2): el SMS es visible si su CustomerId está asignado al actor en la
    // proyección local. IgnoreQueryFilters + tenant explícito en el subquery (scope de Wolverine sin tenant
    // ambiental). Aplica solo cuando assignedToUserId no es null (view_all / flag off ⇒ sin restricción).
    private IQueryable<SmsMessage> ApplyAssignmentFilter(
        IQueryable<SmsMessage> query,
        Guid tenantId,
        Guid? assignedToUserId
    )
    {
        if (assignedToUserId is not { } assignee)
            return query;
        return query.Where(m =>
            db.Set<CustomerAssignmentProjection>()
                .IgnoreQueryFilters()
                .Any(a => a.TenantId == tenantId && a.UserId == assignee && a.CustomerId == m.CustomerId)
        );
    }

    public async Task<PagedResult<SmsMessageSummaryResponse>> SearchMessagesAsync(
        Guid tenantId,
        Guid? customerId,
        SmsMessageStatusFilter status,
        string? term,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? sourceContext,
        int page,
        int size,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    )
    {
        page = page < 1 ? 1 : page;
        size = size is < 1 or > 100 ? 20 : size;

        var query = db.SmsMessages.AsNoTracking().IgnoreQueryFilters().Where(m => m.TenantId == tenantId);
        query = ApplyAssignmentFilter(query, tenantId, assignedToUserId);

        // Alcance del módulo del CRM: solo los SMS que el preparador envió DESDE aquí (crm-sms). Excluye
        // los mensajes de sistema que otros módulos enrutan por el servicio SMS (OTP de firma,
        // notificaciones con SourceContext "notification:sms", etc.) — no son textos del preparador y
        // exponer un OTP en la UI sería una fuga. Null = sin filtro (uso interno/futuro).
        if (!string.IsNullOrWhiteSpace(sourceContext))
            query = query.Where(m => m.SourceContext == sourceContext);

        if (customerId is { } cid)
            query = query.Where(m => m.CustomerId == cid);

        if (MapStatus(status) is { } concrete)
            query = query.Where(m => m.Status == concrete);

        if (fromUtc is { } from)
            query = query.Where(m => m.CreatedAtUtc >= from);
        if (toUtc is { } to)
            query = query.Where(m => m.CreatedAtUtc <= to);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var normalized = term.Trim().ToLowerInvariant();
            query = query.Where(m => m.To.Contains(normalized) || m.Body.ToLower().Contains(normalized));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(m => new SmsMessageSummaryResponse(
                m.Id,
                m.CustomerId,
                m.To,
                m.RecipientName,
                m.Body,
                m.Status,
                m.FailureCode,
                m.Media.Any(),
                m.CreatedAtUtc
            ))
            .ToListAsync(ct);

        return new PagedResult<SmsMessageSummaryResponse>(items, page, size, totalCount);
    }

    public async Task<SmsMessageDetailResponse?> GetMessageByIdAsync(
        Guid tenantId,
        Guid messageId,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    ) =>
        await ApplyAssignmentFilter(
                db.SmsMessages.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(m => m.TenantId == tenantId && m.Id == messageId),
                tenantId,
                assignedToUserId
            )
            .Select(m => new SmsMessageDetailResponse(
                m.Id,
                m.CustomerId,
                m.To,
                m.RecipientName,
                m.Body,
                m.Status,
                m.FailureCode,
                m.FailureReason,
                m.CreatedAtUtc,
                m.AcceptedAtUtc,
                m.DeliveredAtUtc,
                m.FailedAtUtc,
                m.Media.Select(x => new SmsMediaResponse(x.Url, x.ContentType, x.FileName, x.SizeBytes)).ToList()
            ))
            .FirstOrDefaultAsync(ct);

    public async Task<SmsStatsResponse> GetStatsAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        string? sourceContext,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    )
    {
        var scoped = db
            .SmsMessages.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.CreatedAtUtc >= fromUtc && m.CreatedAtUtc <= toUtc);

        // Mismo alcance que el listado: stats de los SMS del preparador (crm-sms), no de los de sistema.
        if (!string.IsNullOrWhiteSpace(sourceContext))
            scoped = scoped.Where(m => m.SourceContext == sourceContext);

        // Mismo criterio de visibilidad por asignación que el listado.
        scoped = ApplyAssignmentFilter(scoped, tenantId, assignedToUserId);

        var byStatus = await scoped
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(SmsMessageStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        var optedOutQuery = db
            .SmsOptOuts.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId && o.Status == SmsOptOutStatus.OptedOut);
        if (assignedToUserId is { } statsAssignee)
            optedOutQuery = optedOutQuery.Where(o =>
                db.Set<CustomerAssignmentProjection>()
                    .IgnoreQueryFilters()
                    .Any(a => a.TenantId == tenantId && a.UserId == statsAssignee && a.CustomerId == o.CustomerId)
            );
        var optedOut = await optedOutQuery.CountAsync(ct);

        return new SmsStatsResponse(
            Total: byStatus.Sum(x => x.Count),
            Pending: CountOf(SmsMessageStatus.Pending),
            Accepted: CountOf(SmsMessageStatus.Accepted),
            Delivered: CountOf(SmsMessageStatus.Delivered),
            Failed: CountOf(SmsMessageStatus.Failed),
            Undeliverable: CountOf(SmsMessageStatus.Undeliverable),
            Suppressed: CountOf(SmsMessageStatus.Suppressed),
            OptedOut: optedOut
        );
    }

    public async Task<PagedResult<SmsOptOutSummaryResponse>> SearchOptOutsAsync(
        Guid tenantId,
        SmsOptOutStatusFilter status,
        string? term,
        int page,
        int size,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    )
    {
        page = page < 1 ? 1 : page;
        size = size is < 1 or > 100 ? 20 : size;

        var query = db.SmsOptOuts.AsNoTracking().IgnoreQueryFilters().Where(o => o.TenantId == tenantId);

        // Visibilidad por asignación: solo las bajas de clientes asignados al actor (mismo criterio que el log).
        if (assignedToUserId is { } assignee)
            query = query.Where(o =>
                db.Set<CustomerAssignmentProjection>()
                    .IgnoreQueryFilters()
                    .Any(a => a.TenantId == tenantId && a.UserId == assignee && a.CustomerId == o.CustomerId)
            );

        query = status switch
        {
            SmsOptOutStatusFilter.OptedOut => query.Where(o => o.Status == SmsOptOutStatus.OptedOut),
            SmsOptOutStatusFilter.Subscribed => query.Where(o => o.Status == SmsOptOutStatus.Subscribed),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(term))
        {
            var normalized = term.Trim().ToLowerInvariant();
            query = query.Where(o => o.PhoneE164.Contains(normalized));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(o => o.UpdatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(o => new SmsOptOutSummaryResponse(
                o.CustomerId,
                o.PhoneE164,
                o.Status,
                o.LastKeyword,
                o.OptedOutAtUtc,
                o.OptedInAtUtc,
                o.UpdatedAtUtc
            ))
            .ToListAsync(ct);

        return new PagedResult<SmsOptOutSummaryResponse>(items, page, size, totalCount);
    }

    private static SmsMessageStatus? MapStatus(SmsMessageStatusFilter filter) =>
        filter switch
        {
            SmsMessageStatusFilter.Pending => SmsMessageStatus.Pending,
            SmsMessageStatusFilter.Accepted => SmsMessageStatus.Accepted,
            SmsMessageStatusFilter.Delivered => SmsMessageStatus.Delivered,
            SmsMessageStatusFilter.Failed => SmsMessageStatus.Failed,
            SmsMessageStatusFilter.Undeliverable => SmsMessageStatus.Undeliverable,
            SmsMessageStatusFilter.Suppressed => SmsMessageStatus.Suppressed,
            _ => null,
        };
}
