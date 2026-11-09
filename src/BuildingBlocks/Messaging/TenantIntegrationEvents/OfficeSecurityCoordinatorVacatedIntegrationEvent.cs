using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.TenantIntegrationEvents;

/// <summary>
/// La designación del responsable de seguridad ("Qualified Individual", FTC Safeguards Rule) de una oficina
/// quedó vacante: Tenant la vació porque el responsable fue retirado. La designación es legal y no se
/// hereda; Notification avisa a quienes administran la oficina (<c>settings.manage</c>) para que designen
/// a otro en Office settings → Compliance.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.TenantIntegrationEvents.OfficeSecurityCoordinatorVacatedIntegrationEvent")]
public sealed record OfficeSecurityCoordinatorVacatedIntegrationEvent : IntegrationEvent
{
    public required Guid OfficeTenantId { get; init; }

    /// <summary>Quién era el responsable (ya no lo es).</summary>
    public required Guid FormerCoordinatorUserId { get; init; }

    /// <summary><see cref="OfficeSecurityCoordinatorVacancyReasons"/>.</summary>
    public required string Reason { get; init; }

    /// <summary>Quién lo provocó (p. ej. el admin que retiró al empleado); null = sistema.</summary>
    public Guid? VacatedByUserId { get; init; }

    public required DateTime VacatedAtUtc { get; init; }
}
