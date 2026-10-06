using BuildingBlocks.Domain;
using TaxVision.Campaigns.Application.Contacts.Abstractions;

namespace TaxVision.Campaigns.Infrastructure.Customers.Directory;

/// <summary>
/// Proyección local del directorio de clientes (event-carried state transfer desde Customer:
/// <c>CustomerCreated/Updated/Archived/Activated/Deactivated/Reactivated</c>). Es la fuente de verdad
/// LOCAL de "quién es cliente" para la audiencia de campañas y la regla contacto⇔cliente — evita llamar
/// la API de Customer en cada lectura. <see cref="Version"/> (el <c>OccurredOn</c> del evento) da
/// idempotencia: solo se aplica un evento más nuevo que el último aplicado.
/// </summary>
public sealed class CustomerDirectoryEntry : TenantEntity
{
    private CustomerDirectoryEntry() { }

    public Guid CustomerId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Email normalizado (minúsculas, trim) — clave natural de lookup contacto⇔cliente.</summary>
    public string Email { get; private set; } = string.Empty;
    public string? PhoneE164 { get; private set; }
    public string Status { get; private set; } = CustomerDirectoryStatus.Active;
    public DateTime Version { get; private set; }

    public static CustomerDirectoryEntry Create(
        Guid tenantId,
        Guid customerId,
        string displayName,
        string email,
        string? phoneE164,
        string status,
        DateTime version
    )
    {
        var entry = new CustomerDirectoryEntry
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            DisplayName = displayName.Trim(),
            Email = Normalize(email),
            PhoneE164 = string.IsNullOrWhiteSpace(phoneE164) ? null : phoneE164.Trim(),
            Status = status,
            Version = version,
        };
        entry.SetTenant(tenantId);
        return entry;
    }

    /// <summary>Datos de identidad (nombre/email/teléfono). No toca el estado. </summary>
    public void UpdateDetails(string displayName, string email, string? phoneE164, DateTime version)
    {
        DisplayName = displayName.Trim();
        Email = Normalize(email);
        PhoneE164 = string.IsNullOrWhiteSpace(phoneE164) ? null : phoneE164.Trim();
        Version = version;
    }

    public void SetStatus(string status, DateTime version)
    {
        Status = status;
        Version = version;
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
