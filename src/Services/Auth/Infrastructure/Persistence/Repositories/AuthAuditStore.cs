using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

/// <summary>
/// Almacenamiento del registro de auditoría de autenticación:
/// escritura de eventos y lectura paginada con filtros.
/// </summary>
public sealed class AuthAuditStore(AuthDbContext db) : IAuthAuditWriter, IAuthAuditReader
{
    public async Task AddAsync(AuthAuditLog log, CancellationToken ct = default) =>
        await db.AuthAuditLogs.AddAsync(log, ct);

    /// <summary>Consulta paginada de eventos de auditoría con filtros opcionales por usuario, acción y rango de fechas.</summary>
    public async Task<(IReadOnlyList<AuthAuditLog> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        Guid? userId,
        string? action,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db.AuthAuditLogs.AsNoTracking().Where(log => log.TenantId == tenantId);

        if (userId is not null)
            query = query.Where(log => log.UserId == userId);
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(log => log.Action == action);
        if (fromUtc is not null)
            query = query.Where(log => log.OccurredAtUtc >= fromUtc);
        if (toUtc is not null)
            query = query.Where(log => log.OccurredAtUtc <= toUtc);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(log => log.OccurredAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return (items, total);
    }
}
