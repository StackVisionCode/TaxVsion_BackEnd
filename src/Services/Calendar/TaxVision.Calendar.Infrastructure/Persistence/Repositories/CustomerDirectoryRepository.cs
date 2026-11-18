using System.Data;
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

    // MERGE set-based de un bloque del import (hasta 500 filas, 3 parámetros cada una, bajo el tope de 2100).
    // Solo actualiza la fila si no tiene un cambio más nuevo.
    public async Task UpsertImportedAsync(
        Guid tenantId,
        IReadOnlyList<ImportedDirectoryEntry> entries,
        DateTime observedAtUtc,
        CancellationToken ct = default
    )
    {
        if (entries.Count == 0)
            return;

        var valuesSql = string.Join(", ", entries.Select((_, i) => $"(@c{i}, @n{i}, @s{i})"));
        var parameters = new List<SqlParameter> { new("@tenantId", tenantId), new("@observedAtUtc", observedAtUtc) };
        for (var i = 0; i < entries.Count; i++)
        {
            parameters.Add(new SqlParameter($"@c{i}", entries[i].CustomerId));
            parameters.Add(new SqlParameter($"@n{i}", SqlDbType.NVarChar, 300) { Value = entries[i].DisplayName });
            parameters.Add(new SqlParameter($"@s{i}", (int)entries[i].Status));
        }

        var sql = $"""
            MERGE INTO CustomerDirectoryEntries WITH (HOLDLOCK) AS target
            USING (VALUES {valuesSql}) AS source(CustomerId, DisplayName, Status)
            ON target.TenantId = @tenantId AND target.CustomerId = source.CustomerId
            WHEN MATCHED AND target.UpdatedAtUtc <= @observedAtUtc THEN
                UPDATE SET DisplayName = source.DisplayName, Status = source.Status, UpdatedAtUtc = @observedAtUtc
            WHEN NOT MATCHED THEN
                INSERT (Id, TenantId, CustomerId, DisplayName, Status, UpdatedAtUtc)
                VALUES (NEWID(), @tenantId, source.CustomerId, source.DisplayName, source.Status, @observedAtUtc);
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
