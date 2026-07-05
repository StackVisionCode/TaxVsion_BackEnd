using BuildingBlocks.Results;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Orquesta la sincronización de una cuenta: resuelve el adaptador, lista/actualiza carpetas, sincroniza
/// mensajes (upsert por MessageId externo), actualiza cursores, escribe el log y publica eventos.
/// Se ejecuta fuera del request HTTP (worker/consumer).
/// </summary>
public interface IEmailSyncService
{
    Task<Result> SyncAccountAsync(Guid accountId, SyncType type, CancellationToken ct = default);
}
