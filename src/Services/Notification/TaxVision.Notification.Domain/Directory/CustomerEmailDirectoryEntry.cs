using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Directory;

/// <summary>
/// Proyección local <c>customerId → email</c>. Hermana de <see cref="UserEmailDirectoryEntry"/> y por
/// el mismo motivo: Notification empezó a recibir eventos cuyo destinatario es un <b>cliente</b>
/// (<c>task.waiting_on_client.v1</c>) y no tenía forma de traducir ese id a una dirección — el
/// directorio de usuarios sólo cubre al personal de la firma.
///
/// <para>
/// El email <b>no viaja en el evento de Task</b> a propósito: Task no lo administra y lo tendría
/// desactualizado. La fuente es Customer, vía sus propios eventos, igual que ya hacen
/// <c>Signature.CustomerEmailProjection</c> y <c>Correspondence.CustomerEmailAddress</c>.
/// </para>
///
/// <para>El email se guarda normalizado (trim + minúsculas) para que el matcheo sea determinístico.</para>
/// </summary>
public sealed class CustomerEmailDirectoryEntry : TenantEntity
{
    private CustomerEmailDirectoryEntry() { }

    public Guid CustomerId { get; private set; }
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Archivado o dado de baja en Customer: hay dirección, pero no corresponde escribirle.</summary>
    public bool IsActive { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static CustomerEmailDirectoryEntry Create(
        Guid tenantId,
        Guid customerId,
        string normalizedEmail,
        string displayName
    )
    {
        var entry = new CustomerEmailDirectoryEntry
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            NormalizedEmail = normalizedEmail,
            DisplayName = displayName,
            IsActive = true,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        entry.SetTenant(tenantId);
        return entry;
    }

    /// <summary>
    /// Un solo punto de entrada para los seis consumers: todos traen la misma foto del cliente y la
    /// diferencia está sólo en si sigue activo.
    /// </summary>
    public void Reconcile(string normalizedEmail, string displayName, bool isActive)
    {
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
            NormalizedEmail = normalizedEmail;

        if (!string.IsNullOrWhiteSpace(displayName))
            DisplayName = displayName;

        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public static string Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
}
