using TaxVision.Calendar.Domain.Availability;

namespace TaxVision.Calendar.Application.Availability.Abstractions;

public interface IAvailabilityRepository
{
    Task<IReadOnlyList<AvailabilityRule>> ListRulesAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Los bloqueos que solapan la ventana, no todos los del usuario.</summary>
    Task<IReadOnlyList<BlockedTime>> ListBlocksAsync(
        Guid tenantId,
        Guid userId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default
    );

    /// <summary>Todos los bloqueos del usuario (sin ventana) — para limpiarlos al retirarlo (offboard). Tracked.</summary>
    Task<IReadOnlyList<BlockedTime>> ListAllBlocksForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );

    /// <summary>Un bloqueo por id (tracked); null si no existe en el tenant.</summary>
    Task<BlockedTime?> FindBlockAsync(Guid tenantId, Guid blockId, CancellationToken ct = default);

    void AddRule(AvailabilityRule rule);

    void AddBlock(BlockedTime block);

    void RemoveBlock(BlockedTime block);
}
