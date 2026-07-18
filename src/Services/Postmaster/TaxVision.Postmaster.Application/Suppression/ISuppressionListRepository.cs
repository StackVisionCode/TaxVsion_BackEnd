using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Suppression;

namespace TaxVision.Postmaster.Application.Suppression;

public interface ISuppressionListRepository
{
    Task AddAsync(SuppressionListEntry entry, CancellationToken ct = default);

    Task<Result<SuppressionListEntry>> GetByAddressAsync(
        Guid tenantId,
        string emailAddress,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<SuppressionListEntry>> ListAsync(
        Guid tenantId,
        string? addressFilter,
        SuppressionReason? reasonFilter,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    /// <summary>Usado por el consumer de envío (Fase 5) — evita N round-trips por destinatario.</summary>
    Task<IReadOnlySet<string>> GetSuppressedAsync(
        Guid tenantId,
        IReadOnlyCollection<string> normalizedAddresses,
        CancellationToken ct = default
    );

    Task<bool> RemoveAsync(Guid tenantId, string emailAddress, CancellationToken ct = default);
}
