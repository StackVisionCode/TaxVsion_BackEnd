using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>
/// Perfil del emisor (los datos de la empresa del tenant: nombre, NIF/RUC, dirección, contacto). Uno
/// por tenant. Se configura una vez y Billing lo congela como <see cref="IssuerSnapshot"/> en cada
/// factura al crearla — así el PDF sale con los datos de la empresa sin que el caller los reenvíe.
/// </summary>
public sealed class IssuerProfile : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? TaxId { get; private set; }
    public Address? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }
    public Guid? LogoFileId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private IssuerProfile() { }

    public static IssuerProfile Create(Guid tenantId, DateTime nowUtc)
    {
        var profile = new IssuerProfile { UpdatedAtUtc = nowUtc };
        profile.SetTenant(tenantId);
        return profile;
    }

    public void Update(
        string name,
        string? taxId,
        Address? address,
        string? phone,
        string? email,
        string? website,
        Guid? logoFileId,
        DateTime nowUtc
    )
    {
        Name = name.Trim();
        TaxId = taxId;
        Address = address;
        Phone = phone;
        Email = email;
        Website = website;
        LogoFileId = logoFileId;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>True si hay al menos un nombre — sin eso no vale la pena estamparlo en la factura.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Name);

    /// <summary>Congela el perfil como el emisor de una factura. La dirección es obligatoria en el
    /// snapshot; si el perfil no tiene una, usa un placeholder para no romper el mapeo.</summary>
    public IssuerSnapshot ToSnapshot() =>
        new(
            Name,
            Address ?? new Address("—", null, string.Empty, string.Empty, string.Empty, "US"),
            Phone,
            Email,
            Website,
            LogoFileId,
            TaxId
        );
}
