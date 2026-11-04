namespace TaxVision.Tenant.Domain.Compliance;

/// <summary>Datos de cumplimiento tal como llegan (sin validar), salvo el EFIN, que viaja aparte ya resuelto.</summary>
public sealed record OfficeComplianceDraft(
    Guid? SecurityCoordinatorUserId,
    string? WispAdoptedOn,
    string? WispLastReviewedOn,
    string? IncidentContactEmail,
    string? IncidentContactPhone,
    int? RetentionYears
);
