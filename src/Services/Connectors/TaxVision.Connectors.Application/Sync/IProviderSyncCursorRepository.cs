using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Application.Sync;

public interface IProviderSyncCursorRepository
{
    Task AddAsync(ProviderSyncCursor cursor, CancellationToken ct = default);

    Task<Result<ProviderSyncCursor>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Purga el cursor al switch de proveedor (IMAP↔Gmail/Graph): el formato del cursor cambia por
    /// proveedor (UIDVALIDITY:UID vs Gmail historyId numérico vs Graph deltaLink), así que un cursor
    /// stale enviado al proveedor nuevo revienta con HTTP 400 "invalid cursor/history id" y el
    /// ReconciliationJob queda bloqueado hasta que se purgue. Default no-op para los fakes.
    /// </summary>
    void Remove(ProviderSyncCursor cursor) { }
}
