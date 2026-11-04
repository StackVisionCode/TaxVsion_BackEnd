using BuildingBlocks.Domain;

namespace TaxVision.Tenant.Domain.Compliance;

/// <summary>
/// Constancia de que un administrador vio el EFIN completo: quién y cuándo. Solo se agrega, nunca se edita
/// ni se borra. Va en su propia tabla y no dentro de <see cref="OfficeCompliance"/>: crece sin límite y se
/// escribe al leer, así que cargarla con el agregado no tendría sentido.
/// </summary>
public sealed class EfinRevealRecord : TenantEntity
{
    private EfinRevealRecord() { }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid RevealedByUserId { get; private set; }
    public DateTime RevealedAtUtc { get; private set; }

    internal static EfinRevealRecord Create(Guid tenantId, Guid revealedByUserId, DateTime nowUtc)
    {
        var record = new EfinRevealRecord { RevealedByUserId = revealedByUserId, RevealedAtUtc = nowUtc };
        record.SetTenant(tenantId);
        return record;
    }
}
