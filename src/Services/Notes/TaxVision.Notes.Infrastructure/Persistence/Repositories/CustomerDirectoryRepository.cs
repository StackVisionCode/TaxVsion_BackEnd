using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Infrastructure.Persistence.Repositories;

// Fase 4B — consumer Wolverine sin TenantContext ambiente (no hay HTTP request) en la mayoría de
// los casos (los consumers de eventos de Customer), mismo criterio que el resto de proyecciones
// de este servicio: IgnoreQueryFilters() explícito, el tenantId ya viene confiable desde el evento.
public sealed class CustomerDirectoryRepository(NotesDbContext db) : ICustomerDirectoryRepository
{
    public Task<bool> ExistsAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        db
            .CustomerDirectoryEntries.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && x.CustomerId == customerId, ct);

    public async Task<string?> GetDisplayNameAsync(Guid tenantId, Guid customerId, CancellationToken ct = default)
    {
        var entry = await db
            .CustomerDirectoryEntries.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.CustomerId == customerId, ct);
        return entry?.DisplayName;
    }

    public Task<CustomerDirectoryEntry?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        db
            .CustomerDirectoryEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.CustomerId == customerId, ct);

    public async Task AddAsync(CustomerDirectoryEntry entry, CancellationToken ct = default) =>
        await db.CustomerDirectoryEntries.AddAsync(entry, ct);

    // Guardrail del plan (03_Plan_De_Fases.md §4B): NUNCA cargar N entidades ni fetch por id para
    // el consumer masivo — MERGE set-based, chunked por el caller (~500). Filas nuevas nacen con
    // DisplayName=NULL (el evento no trae PII) — el job de reconciliación las completa después.
    public async Task UpsertBulkAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        DateTime observedAtUtc,
        CancellationToken ct = default
    )
    {
        if (customerIds.Count == 0)
            return;

        var ids = customerIds.ToArray();
        var valuesSql = string.Join(", ", ids.Select((_, i) => $"(@p{i})"));

        var parameters = new List<SqlParameter> { new("@tenantId", tenantId), new("@observedAtUtc", observedAtUtc) };
        parameters.AddRange(ids.Select((id, i) => new SqlParameter($"@p{i}", id)));

        var sql = $"""
            MERGE INTO CustomerDirectoryEntries WITH (HOLDLOCK) AS target
            USING (VALUES {valuesSql}) AS source(CustomerId)
            ON target.TenantId = @tenantId AND target.CustomerId = source.CustomerId
            WHEN MATCHED THEN
                UPDATE SET UpdatedAtUtc = @observedAtUtc
            WHEN NOT MATCHED THEN
                INSERT (Id, TenantId, CustomerId, DisplayName, Status, UpdatedAtUtc)
                VALUES (NEWID(), @tenantId, source.CustomerId, NULL, 0, @observedAtUtc);
            """;

        await db.Database.ExecuteSqlRawAsync(sql, parameters.ToArray(), ct);
    }

    // IgnoreQueryFilters: job cross-tenant (RBAC Fase 5) — el job de reconciliación necesita el
    // universo de TODOS los tenants con nombres faltantes, nunca sirve una request autenticada.
    public async Task<IReadOnlyList<Guid>> ListTenantIdsWithMissingNamesAsync(
        int limit,
        CancellationToken ct = default
    ) =>
        await db
            .CustomerDirectoryEntries.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.DisplayName == null)
            .Select(x => x.TenantId)
            .Distinct()
            .Take(limit)
            .ToListAsync(ct);

    public async Task ApplyDisplayNameIfMissingAsync(
        Guid tenantId,
        Guid customerId,
        string displayName,
        CancellationToken ct = default
    )
    {
        var entry = await db
            .CustomerDirectoryEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.CustomerId == customerId, ct);
        entry?.ApplyDisplayNameIfMissing(displayName);
    }
}
