using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Domain.Profiles;

/// <summary>
/// Perfil público de la oficina (identidad, contacto, direcciones, idioma, horario y enlaces). Uno por
/// tenant, con el <see cref="TenantId"/> como clave. Todo es opcional: el onboarding no lo crea y cada
/// consumidor cae a su valor actual si falta un dato. Nunca guarda cumplimiento (eso es
/// <see cref="Compliance.OfficeCompliance"/>): este agregado es lo que se publica a los demás servicios.
/// <see cref="Version"/> sube en cada cambio real y ordena los eventos para las proyecciones.
/// </summary>
public sealed class OfficeProfile : ITenantOwned
{
    private readonly List<DailyOpeningHours> _businessHours = [];

    private OfficeProfile() { }

    public Guid TenantId { get; private set; }
    public string? LegalName { get; private set; }

    /// <summary>Nombre comercial (DBA); null = se usa el nombre legal o el de la oficina.</summary>
    public string? DisplayName { get; private set; }
    public OfficeEntityType? EntityType { get; private set; }
    public string? Ein { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }
    public OfficeAddress? PhysicalAddress { get; private set; }

    /// <summary>null = la dirección postal es la misma que la física.</summary>
    public OfficeAddress? MailingAddress { get; private set; }
    public string DefaultLanguage { get; private set; } = OfficeLanguage.English;
    public IReadOnlyList<DailyOpeningHours> BusinessHours => _businessHours;
    public string? BookingUrl { get; private set; }
    public string? ReviewUrl { get; private set; }

    /// <summary>0 = nunca guardado; sube en cada cambio real (los consumidores ignoran ≤ lo que tienen).</summary>
    public long Version { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    /// <summary>Perfil vacío (versión 0). Solo se persiste si un guardado le cambia algo.</summary>
    public static OfficeProfile CreateEmpty(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("An office profile needs a tenant.", nameof(tenantId));

        return new OfficeProfile { TenantId = tenantId };
    }

    /// <summary>
    /// Reemplaza el perfil completo. Valida todo antes de tocar nada; devuelve <c>true</c> si algo cambió
    /// (y subió la versión). Sin cambios no sube la versión ni hay nada que publicar.
    /// </summary>
    public Result<bool> Update(OfficeProfileDraft draft, Guid actorId, DateTime nowUtc)
    {
        var details = OfficeProfileDetails.Create(draft);
        return details.IsFailure
            ? Result.Failure<bool>(details.Error)
            : Result.Success(Apply(details.Value, actorId, nowUtc));
    }

    /// <summary>
    /// Copia única desde el emisor de facturas de Billing (datos anteriores a este perfil). Tolera valores
    /// viejos mal formados: conserva lo válido y devuelve cada campo descartado. Si no queda nada útil no
    /// hay perfil que importar.
    /// </summary>
    public static OfficeProfileImport ImportFromLegacy(
        Guid tenantId,
        OfficeProfileDraft draft,
        Guid actorId,
        DateTime nowUtc
    )
    {
        var dropped = new List<Error>();
        var details = OfficeProfileDetails.CreateLenient(draft, dropped);
        var profile = CreateEmpty(tenantId);
        var imported = profile.Apply(details, actorId, nowUtc);
        return new OfficeProfileImport(imported ? profile : null, dropped);
    }

    // La clave es el tenant: no se reasigna después de crear el perfil.
    void ITenantOwned.SetTenant(Guid tenantId)
    {
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new InvalidOperationException("An office profile cannot move to another tenant.");
        TenantId = tenantId;
    }

    private bool Apply(OfficeProfileDetails details, Guid actorId, DateTime nowUtc)
    {
        if (HasSameContentAs(details))
            return false;

        LegalName = details.LegalName;
        DisplayName = details.DisplayName;
        EntityType = details.EntityType;
        Ein = details.Ein;
        Phone = details.Phone;
        Email = details.Email;
        Website = details.Website;
        PhysicalAddress = details.PhysicalAddress;
        MailingAddress = details.MailingAddress;
        DefaultLanguage = details.DefaultLanguage;
        _businessHours.Clear();
        _businessHours.AddRange(details.BusinessHours.Days);
        BookingUrl = details.BookingUrl;
        ReviewUrl = details.ReviewUrl;

        Version++;
        // Nunca retrocede: si el reloj de otra instancia va atrás, el sello se queda donde estaba.
        UpdatedAtUtc = UpdatedAtUtc is { } previous && previous > nowUtc ? previous : nowUtc;
        UpdatedByUserId = actorId;
        return true;
    }

    private bool HasSameContentAs(OfficeProfileDetails details) =>
        LegalName == details.LegalName
        && DisplayName == details.DisplayName
        && EntityType == details.EntityType
        && Ein == details.Ein
        && Phone == details.Phone
        && Email == details.Email
        && Website == details.Website
        && SameAddress(PhysicalAddress, details.PhysicalAddress)
        && SameAddress(MailingAddress, details.MailingAddress)
        && DefaultLanguage == details.DefaultLanguage
        && details.BusinessHours.Matches(_businessHours)
        && BookingUrl == details.BookingUrl
        && ReviewUrl == details.ReviewUrl;

    private static bool SameAddress(OfficeAddress? current, OfficeAddress? next) =>
        current is null ? next is null : current.Matches(next);
}
