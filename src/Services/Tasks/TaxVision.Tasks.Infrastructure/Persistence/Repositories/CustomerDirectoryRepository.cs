using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

// Casi todas las lecturas de aquí ocurren en consumers o jobs, sin TenantContext ambiente: por eso
// IgnoreQueryFilters() explícito, con el tenantId que ya viene confiable desde el evento o el job.
public sealed class CustomerDirectoryRepository(TasksDbContext db) : ICustomerDirectoryRepository
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

    // MERGE set-based de un bloque del import (hasta 500 filas, 4 parámetros cada una, bajo el tope de 2100).
    // Perfil (nombre/email) y estado se actualizan cada uno si no tiene un cambio más nuevo; un email nulo no borra.
    public async Task UpsertImportedAsync(
        Guid tenantId,
        IReadOnlyList<ImportedDirectoryEntry> entries,
        DateTime observedAtUtc,
        CancellationToken ct = default
    )
    {
        if (entries.Count == 0)
            return;

        var valuesSql = string.Join(", ", entries.Select((_, i) => $"(@c{i}, @n{i}, @e{i}, @s{i})"));
        var parameters = new List<SqlParameter> { new("@tenantId", tenantId), new("@observedAtUtc", observedAtUtc) };
        for (var i = 0; i < entries.Count; i++)
        {
            parameters.Add(new SqlParameter($"@c{i}", entries[i].CustomerId));
            parameters.Add(new SqlParameter($"@n{i}", SqlDbType.NVarChar, 300) { Value = entries[i].DisplayName });
            parameters.Add(
                new SqlParameter($"@e{i}", SqlDbType.NVarChar, 254)
                {
                    Value = (object?)entries[i].PrimaryEmailNormalized ?? DBNull.Value,
                }
            );
            parameters.Add(new SqlParameter($"@s{i}", (int)entries[i].Status));
        }

        var sql = $"""
            MERGE INTO CustomerDirectoryEntries WITH (HOLDLOCK) AS target
            USING (VALUES {valuesSql}) AS source(CustomerId, DisplayName, PrimaryEmailNormalized, Status)
            ON target.TenantId = @tenantId AND target.CustomerId = source.CustomerId
            WHEN MATCHED AND (target.UpdatedAtUtc <= @observedAtUtc
                              OR COALESCE(target.StatusChangedAtUtc, target.UpdatedAtUtc) <= @observedAtUtc) THEN
                UPDATE SET DisplayName = CASE WHEN target.UpdatedAtUtc <= @observedAtUtc
                                              THEN source.DisplayName ELSE target.DisplayName END,
                           PrimaryEmailNormalized = CASE WHEN target.UpdatedAtUtc <= @observedAtUtc
                                              THEN COALESCE(source.PrimaryEmailNormalized, target.PrimaryEmailNormalized)
                                              ELSE target.PrimaryEmailNormalized END,
                           UpdatedAtUtc = CASE WHEN target.UpdatedAtUtc <= @observedAtUtc
                                              THEN @observedAtUtc ELSE target.UpdatedAtUtc END,
                           Status = CASE WHEN COALESCE(target.StatusChangedAtUtc, target.UpdatedAtUtc) <= @observedAtUtc
                                              THEN source.Status ELSE target.Status END,
                           StatusChangedAtUtc = CASE WHEN COALESCE(target.StatusChangedAtUtc, target.UpdatedAtUtc) <= @observedAtUtc
                                              THEN @observedAtUtc ELSE target.StatusChangedAtUtc END
            WHEN NOT MATCHED THEN
                INSERT (Id, TenantId, CustomerId, DisplayName, PrimaryEmailNormalized, Status, UpdatedAtUtc, StatusChangedAtUtc)
                VALUES (NEWID(), @tenantId, source.CustomerId, source.DisplayName, source.PrimaryEmailNormalized,
                        source.Status, @observedAtUtc, @observedAtUtc);
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
