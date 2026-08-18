using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Directory;

/// <summary>
/// Proyección local <c>userId → email</c>. Existe porque Notification recibe eventos que identifican
/// al destinatario por <b>UserId</b> (<c>reminder.due.v1</c>, <c>SecurityAlert</c>, los avisos de
/// CloudStorage) y hasta la Fase 10 de Reminder <b>no tenía forma de traducirlo a una dirección</b>:
/// todos sus consumers de correo sacaban el email del propio evento que consumían.
///
/// <para>
/// <b>Se alimenta de una sola fuente y se invalida por otra.</b> Alta y actualización vienen de
/// <c>UserRegisteredIntegrationEvent</c>, que sí trae el correo. <c>UserProfileUpdated</c> <b>no</b>
/// sirve — solo lleva nombre y apellido — y el cambio de correo confirmado solo publica
/// <c>SecurityAlert{email_changed}</c>, cuyo <c>DetailsJson</c> trae la dirección <b>anterior</b>,
/// no la nueva. Por eso ese evento se trata como <b>invalidación</b>: marca la fila obsoleta y el
/// siguiente envío la repuebla contra Auth. Una fila obsoleta es peor que una ausente — la ausente
/// dispara la recuperación pull, la obsoleta manda el correo a la dirección vieja sin que nadie se
/// entere.
/// </para>
/// </summary>
public sealed class UserEmailDirectoryEntry : TenantEntity
{
    private UserEmailDirectoryEntry() { }

    public Guid UserId { get; private set; }
    public string Email { get; private set; } = string.Empty;

    /// <summary>Lo apaga <c>UserDeactivated</c>: hay dirección, pero no corresponde escribirle.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// <c>true</c> = la dirección guardada dejó de ser confiable (cambio de correo confirmado en
    /// Auth). Se conserva la fila en vez de borrarla para que el rastro de «qué teníamos» no
    /// desaparezca del log de soporte, pero ningún envío la usa hasta que se repuebla.
    /// </summary>
    public bool IsStale { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static UserEmailDirectoryEntry Create(Guid tenantId, Guid userId, string email, bool isActive = true)
    {
        var entry = new UserEmailDirectoryEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Email = email.Trim(),
            IsActive = isActive,
            IsStale = false,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        entry.SetTenant(tenantId);
        return entry;
    }

    public void UpdateEmail(string email, bool isActive)
    {
        Email = email.Trim();
        IsActive = isActive;
        IsStale = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkStale()
    {
        IsStale = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Solo una fila vigente y de un usuario activo sirve para enviarle un correo.</summary>
    public bool IsUsable => !IsStale && IsActive && Email.Length > 0;
}
