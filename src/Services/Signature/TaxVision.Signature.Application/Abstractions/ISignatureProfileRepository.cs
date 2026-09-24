using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Persistencia de las firmas reutilizables del preparador/oficina (F1). El ámbito es
/// (TenantId, OwnerUserId): OwnerUserId con valor = firma personal; null = firma de oficina.
/// </summary>
public interface ISignatureProfileRepository
{
    Task<SignatureProfile?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Firmas visibles para un usuario: siempre las de oficina (OwnerUserId == null) y, solo si
    /// <paramref name="includePersonal"/>, también las suyas. Con el toggle de firma propia apagado
    /// para un empleado no-admin, se pasa false y quedan solo las de oficina.
    /// </summary>
    Task<IReadOnlyList<SignatureProfile>> ListVisibleAsync(
        Guid tenantId,
        Guid userId,
        bool includePersonal,
        bool includeArchived,
        CancellationToken ct = default
    );

    /// <summary>Firmas de un ámbito exacto (mismo OwnerUserId), para gestionar el "por defecto".</summary>
    Task<IReadOnlyList<SignatureProfile>> ListByOwnerAsync(
        Guid tenantId,
        Guid? ownerUserId,
        bool includeArchived,
        CancellationToken ct = default
    );

    /// <summary>La firma por defecto (no archivada) de un ámbito exacto, o null si no hay.</summary>
    Task<SignatureProfile?> GetDefaultAsync(Guid tenantId, Guid? ownerUserId, CancellationToken ct = default);

    Task AddAsync(SignatureProfile profile, CancellationToken ct = default);

    void Remove(SignatureProfile profile);
}
