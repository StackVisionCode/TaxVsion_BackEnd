using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Domain.Projections;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// El MERGE de un bloque del import contra SQL Server: crea con nombre y email, actualiza solo si no hay un cambio más
/// nuevo y nunca borra un email con nulo. Bloque completo de 500 para probar el tope de parámetros.
/// </summary>
public sealed class CustomerDirectoryImportIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task A_full_chunk_is_inserted_with_name_email_and_status()
    {
        var entries = Enumerable
            .Range(0, 500)
            .Select(i => new ImportedDirectoryEntry(
                Guid.NewGuid(),
                $"Client {i}",
                $"c{i}@example.com",
                CustomerDirectoryStatus.Active
            ))
            .ToList();

        await UpsertAsync(entries, T0);

        await using var db = _database.NewContext(_tenantId);
        var rows = await db.CustomerDirectoryEntries.ToListAsync();
        Assert.Equal(500, rows.Count);
        var first = rows.Single(r => r.CustomerId == entries[0].CustomerId);
        Assert.Equal("Client 0", first.DisplayName);
        Assert.Equal("c0@example.com", first.PrimaryEmailNormalized);
        Assert.Equal(T0, first.UpdatedAtUtc);
    }

    [Fact]
    public async Task Existing_rows_change_only_when_the_chunk_is_newer_and_a_null_email_keeps_the_known_one()
    {
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await using (var seed = _database.NewContext(_tenantId))
        {
            seed.CustomerDirectoryEntries.AddRange(
                CustomerDirectoryEntry.Create(
                    _tenantId,
                    older,
                    "Old",
                    CustomerDirectoryStatus.Active,
                    T0.AddMinutes(-5),
                    "keep@example.com"
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
                new ImportedDirectoryEntry(older, "Updated", null, CustomerDirectoryStatus.Active),
                new ImportedDirectoryEntry(newer, "From import", "x@example.com", CustomerDirectoryStatus.Active),
            ],
            T0
        );

        await using var db = _database.NewContext(_tenantId);
        var updated = await db.CustomerDirectoryEntries.SingleAsync(r => r.CustomerId == older);
        Assert.Equal("Updated", updated.DisplayName);
        Assert.Equal("keep@example.com", updated.PrimaryEmailNormalized);
        Assert.Equal(T0, updated.UpdatedAtUtc);
        var untouched = await db.CustomerDirectoryEntries.SingleAsync(r => r.CustomerId == newer);
        Assert.Equal("Archived", untouched.DisplayName);
        Assert.Equal(CustomerDirectoryStatus.Archived, untouched.Status);
        Assert.Null(untouched.PrimaryEmailNormalized);
    }

    // F-E: nombre/email y estado se ordenan por separado: una baja más nueva no bloquea el email de la ficha.
    [Fact]
    public async Task A_newer_status_change_keeps_its_status_but_the_card_still_brings_the_email()
    {
        var customerId = Guid.NewGuid();
        await using (var seed = _database.NewContext(_tenantId))
        {
            var entry = CustomerDirectoryEntry.Create(
                _tenantId,
                customerId,
                "Client",
                CustomerDirectoryStatus.Active,
                T0.AddMinutes(-5)
            );
            entry.ApplyStatusIfNewer(CustomerDirectoryStatus.Inactive, T0.AddMinutes(5));
            seed.CustomerDirectoryEntries.Add(entry);
            await seed.SaveChangesAsync();
        }

        await UpsertAsync(
            [new ImportedDirectoryEntry(customerId, "Client", "late@example.com", CustomerDirectoryStatus.Active)],
            T0
        );

        await using var db = _database.NewContext(_tenantId);
        var row = await db.CustomerDirectoryEntries.SingleAsync(r => r.CustomerId == customerId);
        Assert.Equal("late@example.com", row.PrimaryEmailNormalized);
        Assert.Equal(CustomerDirectoryStatus.Inactive, row.Status);
    }

    private async Task UpsertAsync(IReadOnlyList<ImportedDirectoryEntry> entries, DateTime observedAtUtc)
    {
        await using var db = _database.NewContext(_tenantId);
        await new CustomerDirectoryRepository(db).UpsertImportedAsync(_tenantId, entries, observedAtUtc);
    }
}
