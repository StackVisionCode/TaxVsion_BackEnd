using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Application.Compliance.Abstractions;

/// <summary>Cifra y abre el EFIN con la clave maestra rotativa del servicio. El dominio nunca ve la clave.</summary>
public interface IEfinProtector
{
    EncryptedEfin Protect(Efin efin);

    /// <summary>EFIN en claro. Usarlo solo para revelarlo (auditado) o para comparar con uno nuevo.</summary>
    string Reveal(EncryptedEfin encrypted);
}
