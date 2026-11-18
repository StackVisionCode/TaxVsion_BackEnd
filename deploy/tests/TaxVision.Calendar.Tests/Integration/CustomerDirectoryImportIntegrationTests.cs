using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Domain.Projections;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// El MERGE de un bloque del import contra SQL Server: crea con nombre, actualiza solo si no hay un cambio más nuevo.
/// Bloque completo de 500 para probar el tope de parámetros.
/// </summary>
public sealed class CustomerDirectoryImportIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task A_full_chunk_is_inserted_with_name_and_status()
    {
        var entries = Enumerable
            .Range(0, 500)
            .Select(i => new ImportedDirectoryEntry(Guid.NewGuid(), $"Client {i}", CustomerDirectoryStatus.Active))
            .ToList();

        await UpsertAsync(entries, T0);

        await using var db = NewContext();
        var rows = await db
            .CustomerDirectoryEntries.IgnoreQueryFilters()
            .Where(r => r.TenantId == _tenantId)
            .ToListAsync();
        Assert.Equal(500, rows.Count);
        var first = rows.Single(r => r.CustomerId == entries[0].CustomerId);
        Assert.Equal("Client 0", first.DisplayName);
        Assert.Equal(T0, first.UpdatedAtUtc);
    }

    [Fact]
    public async Task Existing_rows_change_only_when_the_chunk_is_newer()
    {
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.CustomerDirectoryEntries.AddRange(
                CustomerDirectoryEntry.Create(
                    _tenantId,
                    older,
                    "Old",
                    CustomerDirectoryStatus.Active,
                    T0.AddMinutes(-5)
                ),
                CustomerDirectoryEntry.Create(
                    _tenantId,
                    newer,
                    "Archived",
                    CustomerDirectoryStatus.Archived,
                    T0.AddMinutes(5)
                )
            );
            await seed.SaveChangesAsync();
        }

        await UpsertAsync(
            [
                new ImportedDirectoryEntry(older, "Updated", CustomerDirectoryStatus.Active),
                new ImportedDirectoryEntry(newer, "From import", CustomerDirectoryStatus.Active),
            ],
            T0
        );

        await using var db = NewContext();
        var rows = await db
            .CustomerDirectoryEntries.IgnoreQueryFilters()
            .Where(r => r.TenantId == _tenantId)
            .ToListAsync();
        var updated = rows.Single(r => r.CustomerId == older);
        Assert.Equal("Updated", updated.DisplayName);
        Assert.Equal(T0, updated.UpdatedAtUtc);
        var untouched = rows.Single(r => r.CustomerId == newer);
        Assert.Equal("Archived", untouched.DisplayName);
        Assert.Equal(CustomerDirectoryStatus.Archived, untouched.Status);
    }

    private async Task UpsertAsync(IReadOnlyList<ImportedDirectoryEntry> entries, DateTime observedAtUtc)
    {
        await using var db = NewContext();
        await new CustomerDirectoryRepository(db).UpsertImportedAsync(_tenantId, entries, observedAtUtc);
    }

    private CalendarDbContext NewContext() => _database.NewContext();
}
