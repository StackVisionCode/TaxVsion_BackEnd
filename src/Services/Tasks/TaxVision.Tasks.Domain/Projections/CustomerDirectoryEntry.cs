using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Projections;

/// <summary>
/// Proyección local delgada de un customer del tenant, alimentada por los eventos de Customer y por
/// los bloques del import. Task la usa para mostrar y buscar por cliente sin llamar a Customer,
/// y para la validación *soft* de <c>CustomerId</c> al crear una tarea.
///
/// <para>
/// <see cref="DisplayName"/> es nullable a propósito: el backfill o un evento sin nombre pueden crear
/// la fila antes de conocerlo, y el job de reconciliación lo rellena después. El nombre es cosmético; lo que importa es que el customer exista.
/// </para>
/// </summary>
public sealed class CustomerDirectoryEntry : ITenantOwned
{
    private CustomerDirectoryEntry() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string? DisplayName { get; private set; }

    // Email principal normalizado (trim + minúsculas, como Customer): Client Forms lo busca sin llamar a Customer.
    public string? PrimaryEmailNormalized { get; private set; }
    public CustomerDirectoryStatus Status { get; private set; }

    /// <summary>Hora del último nombre/email aplicado (ordena el perfil).</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Hora del último estado aplicado. Va aparte del perfil: un cambio de estado más nuevo no debe bloquear el email
    /// que llega después del alta. Nulo en filas viejas: cuenta como <see cref="UpdatedAtUtc"/>.
    /// </summary>
    public DateTime? StatusChangedAtUtc { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static CustomerDirectoryEntry Create(
        Guid tenantId,
        Guid customerId,
        string? displayName,
        CustomerDirectoryStatus status,
        DateTime observedAtUtc,
        string? primaryEmail = null
    )
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (customerId == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(customerId));

        var entry = new CustomerDirectoryEntry
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            DisplayName = displayName,
            PrimaryEmailNormalized = NormalizeEmail(primaryEmail),
            Status = status,
            UpdatedAtUtc = observedAtUtc,
            StatusChangedAtUtc = observedAtUtc,
        };
        entry.SetTenant(tenantId);
        return entry;
    }

    /// <summary>
    /// Idempotente en dos sentidos: no retrocede si <paramref name="observedAtUtc"/> es más viejo que
    /// lo ya aplicado —los eventos de Customer no traen número de revisión, así que el orden lo da el
    /// tiempo de ocurrencia— y nunca pisa con <c>null</c> un nombre ya conocido, porque un evento de
    /// cambio de status llega sin nombre y borrarían el que ya se reconcilió.
    /// </summary>
    public void ApplyIfNewer(
        string? displayName,
        CustomerDirectoryStatus status,
        DateTime observedAtUtc,
        string? primaryEmail = null
    )
    {
        ApplyProfileIfNewer(displayName, primaryEmail, observedAtUtc);
        ApplyStatusIfNewer(status, observedAtUtc);
    }

    /// <summary>Nombre y email, ordenados por su propia hora. Un nulo nunca borra lo conocido.</summary>
    public void ApplyProfileIfNewer(string? displayName, string? primaryEmail, DateTime observedAtUtc)
    {
        if (observedAtUtc < UpdatedAtUtc)
            return;

        if (displayName is not null)
            DisplayName = displayName;
        if (NormalizeEmail(primaryEmail) is { } email)
            PrimaryEmailNormalized = email;
        UpdatedAtUtc = observedAtUtc;
    }

    /// <summary>Estado, ordenado por su propia hora: un estado más viejo que el último aplicado no lo revierte.</summary>
    public void ApplyStatusIfNewer(CustomerDirectoryStatus status, DateTime observedAtUtc)
    {
        if (observedAtUtc < (StatusChangedAtUtc ?? UpdatedAtUtc))
            return;

        Status = status;
        StatusChangedAtUtc = observedAtUtc;
    }

    // Misma normalización que EmailAddress de Customer; null si no parece un email.
    public static string? NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > 254 || !trimmed.Contains('@')
            ? null
            : trimmed.ToLowerInvariant();
    }

    /// <summary>Sólo para el job de reconciliación de nombres: no toca el status ni retrocede en el tiempo.</summary>
    public void ApplyDisplayNameIfMissing(string displayName)
    {
        if (DisplayName is not null)
            return;
        DisplayName = displayName;
    }
}
