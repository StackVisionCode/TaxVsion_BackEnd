using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Connectors.Domain.Sync;

/// <summary>
/// Cursor persistente para retomar sync incremental — único por AccountId. Valor opaco: HistoryId
/// (Gmail), DeltaLink completo (Graph) o "UidValidity:LastUid" (IMAP), interpretado únicamente por
/// el IEmailProviderClient del proveedor correspondiente (ver GetHistoryAsync). Null en cuentas
/// recién creadas antes del primer sync.
/// </summary>
public sealed class ProviderSyncCursor : BaseEntity
{
    private ProviderSyncCursor() { }

    public Guid AccountId { get; private set; }
    public string? CursorValue { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Token de concurrencia optimista (SQL Server rowversion) — defensa en profundidad detrás del
    /// IDistributedLock por-cuenta que ProcessGmailPushNotificationHandler/ProcessGraphNotificationHandler
    /// ya toman (Fase 4 de hardening): el lock es la protección primaria contra el read-modify-write
    /// concurrente de dos deliveries at-least-once para la misma cuenta, pero su TTL puede expirar a
    /// mitad de un procesamiento genuinamente lento. Con IsRowVersion(), ese escenario (raro, ya
    /// cubierto por el lock en el caso normal) hace que el segundo SaveChanges en conflicto tire
    /// DbUpdateConcurrencyException en vez de pisar el cursor del primero — mismo patrón que
    /// ShareLink.RowVersion en CloudStorage.
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    public static Result<ProviderSyncCursor> Create(Guid accountId, string? cursorValue, DateTime nowUtc)
    {
        if (accountId == Guid.Empty)
            return Result.Failure<ProviderSyncCursor>(
                new Error("ProviderSyncCursor.AccountId", "AccountId is required.")
            );

        return Result.Success(
            new ProviderSyncCursor
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                CursorValue = cursorValue,
                UpdatedAtUtc = nowUtc,
            }
        );
    }

    public void UpdateCursor(string? cursorValue, DateTime updatedAtUtc)
    {
        CursorValue = cursorValue;
        UpdatedAtUtc = updatedAtUtc;
    }
}
