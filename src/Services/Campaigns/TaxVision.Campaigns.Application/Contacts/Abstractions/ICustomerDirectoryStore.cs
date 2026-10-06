namespace TaxVision.Campaigns.Application.Contacts.Abstractions;

/// <summary>Estados del cliente en la proyección local (reflejan el ciclo de vida en Customer).</summary>
public static class CustomerDirectoryStatus
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Archived = "Archived";
}

/// <summary>Un cliente del directorio local (proyección) — lo mínimo para audiencia y regla contacto⇔cliente.</summary>
public sealed record CustomerDirectoryRecord(
    Guid CustomerId,
    string DisplayName,
    string Email,
    string? PhoneE164,
    string Status
);

/// <summary>
/// Store de la proyección local del directorio de clientes (mantenida por los consumers de los eventos de
/// Customer). Escrituras = aplicar eventos (version-guarded); lecturas = audiencia + "¿este email ya es
/// cliente?". El caller hace <c>SaveChangesAsync</c> (igual que la proyección de asignaciones).
/// </summary>
public interface ICustomerDirectoryStore
{
    /// <summary>Última <c>Version</c> aplicada para ese cliente, o null si no existe en la proyección.</summary>
    Task<DateTime?> GetVersionAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    /// <summary>Alta/actualización completa (evento Created): nombre+email+teléfono+estado.</summary>
    Task UpsertAsync(
        Guid tenantId,
        Guid customerId,
        string displayName,
        string email,
        string? phoneE164,
        string status,
        DateTime version,
        CancellationToken ct = default
    );

    /// <summary>Actualiza datos de identidad (evento Updated) sin tocar el estado; si falta la fila, la crea Activa.</summary>
    Task UpdateDetailsAsync(
        Guid tenantId,
        Guid customerId,
        string displayName,
        string email,
        string? phoneE164,
        DateTime version,
        CancellationToken ct = default
    );

    /// <summary>Cambia solo el estado (Archived/Inactive/Active). Si falta la fila, no hace nada (llegará por reconciliación/Created).</summary>
    Task SetStatusAsync(Guid tenantId, Guid customerId, string status, DateTime version, CancellationToken ct = default);

    /// <summary>¿Existe un cliente con ese email (normalizado) en el tenant? Para la regla contacto⇔cliente.</summary>
    Task<CustomerDirectoryRecord?> FindByEmailAsync(Guid tenantId, string email, CancellationToken ct = default);

    /// <summary>Clientes activos del tenant (audiencia). Vacío si la proyección aún no se sembró.</summary>
    Task<IReadOnlyList<CustomerDirectoryRecord>> ListActiveAsync(Guid tenantId, CancellationToken ct = default);
}
