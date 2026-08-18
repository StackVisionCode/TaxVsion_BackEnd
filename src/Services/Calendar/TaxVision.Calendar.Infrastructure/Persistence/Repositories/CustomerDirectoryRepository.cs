using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Domain.Projections;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

// Casi todas las lecturas de aquí ocurren en consumers o jobs, sin TenantContext ambiente: por eso
// IgnoreQueryFilters() explícito, con el tenantId que ya viene confiable desde el evento o el job.
public sealed class CustomerDirectoryRepository(CalendarDbContext db) : ICustomerDirectoryRepository
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

    // MERGE set-based: el import masivo puede traer miles de ids y cargarlos como entidades no es
    // opción. Las filas nuevas nacen sin nombre porque el evento no trae PII.
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
