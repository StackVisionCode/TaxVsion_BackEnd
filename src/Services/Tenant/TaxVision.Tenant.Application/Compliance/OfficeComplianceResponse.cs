using TaxVision.Tenant.Domain.Compliance;

namespace TaxVision.Tenant.Application.Compliance;

/// <summary>Cumplimiento de la oficina (solo administradores). El EFIN sale siempre enmascarado.</summary>
public sealed record OfficeComplianceResponse(
    bool HasEfin,
    string? EfinMasked,
    Guid? SecurityCoordinatorUserId,
    DateOnly? WispAdoptedOn,
    DateOnly? WispLastReviewedOn,
    string? IncidentContactEmail,
    string? IncidentContactPhone,
    int? RetentionYears,
    DateTime? UpdatedAtUtc
)
{
    public static OfficeComplianceResponse From(OfficeCompliance? compliance) =>
        compliance is null
            ? new(false, null, null, null, null, null, null, null, null)
            : new(
                compliance.Efin is not null,
                compliance.Efin?.Masked,
                compliance.SecurityCoordinatorUserId,
                compliance.WispAdoptedOn,
                compliance.WispLastReviewedOn,
                compliance.IncidentContactEmail,
                compliance.IncidentContactPhone,
                compliance.RetentionYears,
                compliance.UpdatedAtUtc
            );
}
