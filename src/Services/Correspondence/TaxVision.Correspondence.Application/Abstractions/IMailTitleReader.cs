namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Asuntos por id para los títulos en vivo del historial, con las mismas reglas de visibilidad que el Inbox y Sent:
/// gate de buzón (<c>null</c> = todos) y cliente asignado (<c>null</c> = todos). Lo que no se ve no vuelve.
/// </summary>
public interface IMailTitleReader
{
    /// <summary>Enviados vivos (no borradores ni en la papelera).</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetSentSubjectsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> draftIds,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        CancellationToken ct = default
    );

    Task<IReadOnlyDictionary<Guid, string>> GetThreadSubjectsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> threadIds,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        CancellationToken ct = default
    );
}
