using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>
/// Perfil del emisor (nombre legal, EIN, dirección y contacto de la oficina). Uno por tenant. Desde la F1
/// es una PROYECCIÓN del perfil de la oficina que vive en Tenant: solo lo escribe
/// <see cref="ApplyTenantProfile"/> al recibir TenantProfileChanged. Billing lo congela como
/// <see cref="IssuerSnapshot"/> en cada factura al crearla, así el PDF sale con los datos de la oficina.
/// </summary>
public sealed class IssuerProfile : TenantEntity
{
    // Largos de las columnas (IssuerProfileConfiguration); un valor más largo se recorta, nunca falla.
    public const int NameMaxLength = 256;
    public const int TaxIdMaxLength = 64;
    public const int PhoneMaxLength = 64;
    public const int EmailMaxLength = 256;
    public const int WebsiteMaxLength = 256;

    public string Name { get; private set; } = string.Empty;
    public string? TaxId { get; private set; }
    public Address? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }
    public Guid? LogoFileId { get; private set; }

    /// <summary>Moneda por defecto del tenant para nuevas facturas (ISO-4217, ej. "USD"). Prellenado del
    /// formulario; cada factura CONGELA su moneda al crearse, así el histórico nunca cambia aunque esto
    /// cambie luego. "Platform fallback" = si el tenant no la fijó, se usa "USD".</summary>
    public string DefaultCurrency { get; private set; } = "USD";

    /// <summary>Versión del perfil de Tenant ya aplicada (0 = nunca llegó un evento). Un evento con versión
    /// menor o igual se ignora: así un mensaje viejo o repetido no pisa uno más nuevo.</summary>
    public long SourceVersion { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private IssuerProfile() { }

    public static IssuerProfile Create(Guid tenantId, DateTime nowUtc)
    {
        var profile = new IssuerProfile { UpdatedAtUtc = nowUtc };
        profile.SetTenant(tenantId);
        return profile;
    }

    /// <summary>
    /// Aplica el perfil de la oficina publicado por Tenant (estado completo). Devuelve false si la versión
    /// no es más nueva que la aplicada. Nombre = legal, si no el comercial, si no se conserva el actual
    /// (Billing no conoce otro nombre de la oficina). El resto se copia tal cual, null incluido. Mapeo
    /// tolerante: Tenant ya validó, y un valor raro solo se recorta al largo de la columna. El logo y la
    /// moneda no se tocan: no vienen del perfil.
    /// </summary>
    public bool ApplyTenantProfile(long version, TenantOfficeProfile source, DateTime nowUtc)
    {
        if (version <= SourceVersion)
            return false;

        var name = Fit(source.LegalName, NameMaxLength) ?? Fit(source.DisplayName, NameMaxLength);
        if (name is not null)
            Name = name;

        TaxId = Fit(source.TaxId, TaxIdMaxLength);
        Address = source.Address;
        Phone = Fit(source.Phone, PhoneMaxLength);
        Email = Fit(source.Email, EmailMaxLength);
        Website = Fit(source.Website, WebsiteMaxLength);
        SourceVersion = version;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>True si hay al menos un nombre — sin eso no vale la pena estamparlo en la factura.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Name);

    /// <summary>Congela el perfil como el emisor de una factura. La dirección es obligatoria en el
    /// snapshot; si el perfil no tiene una, va vacía y el PDF no imprime línea de dirección.</summary>
    public IssuerSnapshot ToSnapshot() =>
        new(Name, Address ?? Address.Empty(), Phone, Email, Website, LogoFileId, TaxId);

    private static string? Fit(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

/// <summary>Datos del perfil de la oficina que proyecta Billing, ya mapeados desde el evento de Tenant.</summary>
public sealed record TenantOfficeProfile(
    string? LegalName,
    string? DisplayName,
    string? TaxId,
    Address? Address,
    string? Phone,
    string? Email,
    string? Website
);
