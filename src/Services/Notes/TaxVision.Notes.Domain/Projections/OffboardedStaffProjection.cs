using BuildingBlocks.Domain;

namespace TaxVision.Notes.Domain.Projections;

/// <summary>
/// Registro local de empleados RETIRADOS (offboarded) del tenant, alimentado por
/// <c>UserOffboardedIntegrationEvent</c> (punto 3.2). Habilita el manage-override de edición: un staff
/// con <c>notes.view_all</c> puede gestionar el contenido de una nota cuyo AUTOR fue retirado y ya no
/// puede hacerlo él mismo. Una desactivación temporal NO cuenta — offboard es terminal, por eso solo
/// se alimenta del evento de offboard, no de deactivate. No guarda PII más allá del Guid; la autoría
/// de la nota (<c>CreatedByUserId</c>) nunca se reescribe.
/// </summary>
public sealed class OffboardedStaffProjection : TenantEntity
{
    private OffboardedStaffProjection() { }

    public Guid UserId { get; private set; }
    public DateTime OffboardedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static OffboardedStaffProjection Create(Guid tenantId, Guid userId, DateTime offboardedAtUtc)
    {
        var projection = new OffboardedStaffProjection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OffboardedAtUtc = offboardedAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
        };
        projection.SetTenant(tenantId);
        return projection;
    }
}
