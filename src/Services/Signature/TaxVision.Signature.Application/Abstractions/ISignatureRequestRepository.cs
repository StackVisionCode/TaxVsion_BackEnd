using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Repositorio del aggregate root <see cref="SignatureRequest"/>. Todas las lecturas
/// filtran por <c>TenantId</c> — el aislamiento multitenant se hace a nivel de repo,
/// no de servicio; nunca se aceptan queries sin tenant.
/// </summary>
public interface ISignatureRequestRepository
{
    /// <summary>
    /// Devuelve la solicitud con sus firmantes y campos cargados. Retorna
    /// <c>null</c> si no existe para el tenant.
    /// </summary>
    Task<SignatureRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve los borradores del tenant cuyo <c>OriginalFileId</c> coincide con
    /// <paramref name="fileId"/>. Se usa al recibir <c>FileAvailable</c> para promover
    /// automáticamente a <c>Ready</c>.
    /// </summary>
    Task<IReadOnlyList<SignatureRequest>> ListDraftsWaitingForFileAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Solicitudes cuyo <c>ExpiresAtUtc</c> ya pasó y aún no están en estado terminal.
    /// Consumida por el <c>ExpirationScheduler</c> — filtro global tenant desactivado
    /// para permitir escaneo cross-tenant desde el background job.
    /// </summary>
    Task<IReadOnlyList<SignatureRequest>> ListExpiredCandidatesAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Solicitudes InProgress cuya expiración se acerca (dentro de <paramref name="withinWindow"/>)
    /// y que aún no han recibido un reminder reciente (<paramref name="cooldown"/>).
    /// Consumida por el <c>ReminderScheduler</c>.
    /// </summary>
    Task<IReadOnlyList<SignatureRequest>> ListReminderCandidatesAsync(
        DateTime nowUtc,
        TimeSpan withinWindow,
        TimeSpan cooldown,
        int maxReminders,
        CancellationToken ct = default
    );

    /// <summary>
    /// Solicitudes en estado terminal (<c>Completed</c>/<c>Rejected</c>/<c>Canceled</c>/<c>Expired</c>)
    /// cuya última actualización es más antigua que la política de retención, y que NO tienen
    /// <c>LegalHold</c> activo. Consumida por el <c>PurgeScheduler</c>.
    /// </summary>
    Task<IReadOnlyList<SignatureRequest>> ListPurgeCandidatesAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    );

    Task AddAsync(SignatureRequest request, CancellationToken ct = default);

    void Remove(SignatureRequest request);
}
