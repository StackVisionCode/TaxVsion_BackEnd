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

    [UserReference(
        UserReferenceKind.Projection,
        HandledBy = "UserEmailDirectoryOffboardingConsumer",
        Note = "Inactivo = el despachador corta correo, SMS, push e in-app."
    )]
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

    /// <summary>
    /// <c>OccurredOn</c> del último evento de Auth aplicado. Un evento más viejo se ignora: Auth no
    /// garantiza orden y una desactivación atrasada silenciaba a un usuario ya reactivado.
    /// </summary>
    public DateTime? LastSourceEventAtUtc { get; private set; }

    /// <summary>Idioma elegido por el usuario (<c>en</c>/<c>es</c>). Null = sin preferencia (respaldo).</summary>
    public string? Language { get; private set; }

    /// <summary>Zona propia del usuario; solo se usa como zona del lector en correos (D-N40).</summary>
    public string? TimeZoneId { get; private set; }

    public string? DisplayName { get; private set; }

    /// <summary><c>OccurredOn</c> de la última foto de perfil aplicada: descarta las más viejas.</summary>
    public DateTime? ProfileSourceAtUtc { get; private set; }

    private static readonly string[] SupportedLanguages = ["en", "es"];

    /// <returns><c>false</c> si la foto es más vieja que la guardada (no cambia nada).</returns>
    public bool ApplyProfile(string? language, string? timeZoneId, string? displayName, DateTime occurredOnUtc)
    {
        if (ProfileSourceAtUtc is { } last && occurredOnUtc < last)
            return false;
        var normalized = language?.Trim().ToLowerInvariant();
        Language = normalized is not null && SupportedLanguages.Contains(normalized) ? normalized : null;
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
        if (!string.IsNullOrWhiteSpace(displayName))
            DisplayName = displayName.Trim().Length > 160 ? displayName.Trim()[..160] : displayName.Trim();
        ProfileSourceAtUtc = occurredOnUtc;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    public static UserEmailDirectoryEntry Create(
        Guid tenantId,
        Guid userId,
        string email,
        bool isActive = true,
        DateTime? sourceEventAtUtc = null
    )
    {
        var entry = new UserEmailDirectoryEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Email = email.Trim(),
            IsActive = isActive,
            IsStale = false,
            UpdatedAtUtc = DateTime.UtcNow,
            LastSourceEventAtUtc = sourceEventAtUtc,
        };
        entry.SetTenant(tenantId);
        return entry;
    }

    /// <returns><c>false</c> si el evento es más viejo que el último aplicado (no cambia nada).</returns>
    public bool UpdateEmail(string email, bool isActive, DateTime sourceEventAtUtc)
    {
        if (!Accepts(sourceEventAtUtc))
            return false;
        Email = email.Trim();
        IsActive = isActive;
        IsStale = false;
        return true;
    }

    public bool MarkStale(DateTime sourceEventAtUtc)
    {
        if (!Accepts(sourceEventAtUtc))
            return false;
        IsStale = true;
        return true;
    }

    public bool SetActive(bool isActive, DateTime sourceEventAtUtc)
    {
        if (!Accepts(sourceEventAtUtc))
            return false;
        IsActive = isActive;
        return true;
    }

    // Igual que ApplyIfNewer de permisos: el empate se aplica (reentrega idempotente).
    private bool Accepts(DateTime sourceEventAtUtc)
    {
        if (LastSourceEventAtUtc is { } last && sourceEventAtUtc < last)
            return false;
        LastSourceEventAtUtc = sourceEventAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>Solo una fila vigente y de un usuario activo sirve para enviarle un correo.</summary>
    public bool IsUsable => !IsStale && IsActive && Email.Length > 0;
}
