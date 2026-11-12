using TaxVision.Subscription.Domain.Migrations;

namespace TaxVision.Subscription.Application.Abstractions;

/// <summary>Cuánto almacenamiento usa una oficina, según CloudStorage (M2M). Null si no respondió.</summary>
public interface IStorageUsageClient
{
    Task<long?> GetUsedBytesAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Lo que una oficina tiene además de su plan: asientos comprados vivos y la cuota de su foto actual.</summary>
public sealed record OfficeHoldings(int LiveSeats, long? SnapshotStorageMaxBytes);

/// <summary>Lecturas entre oficinas para las migraciones (solo el admin de plataforma). Cruza tenants a propósito.</summary>
public interface ISubscriberMigrationReads
{
    Task<IReadOnlyDictionary<Guid, OfficeHoldings>> GetHoldingsAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct = default
    );
}

public interface ISubscriberMigrationRepository
{
    Task AddAsync(SubscriberMigration migration, CancellationToken ct = default);

    Task<SubscriberMigration?> GetAsync(Guid migrationId, CancellationToken ct = default);

    Task<SubscriberMigration?> GetForUpdateAsync(Guid migrationId, CancellationToken ct = default);

    /// <summary>Migraciones con oficinas por mover (corriendo) o por desagendar (canceladas).</summary>
    Task<IReadOnlyList<SubscriberMigration>> ListWithWorkAsync(CancellationToken ct = default);
}
