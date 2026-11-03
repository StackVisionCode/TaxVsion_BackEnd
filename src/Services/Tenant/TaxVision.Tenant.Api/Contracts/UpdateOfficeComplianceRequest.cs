using TaxVision.Tenant.Domain.Compliance;

namespace TaxVision.Tenant.Api.Contracts;

/// <summary>
/// <c>efin</c>: omitido o null = no tocar; <c>""</c> = borrar; si no, 6 dígitos. Fechas <c>yyyy-MM-dd</c>.
/// </summary>
public sealed record UpdateOfficeComplianceRequest(
    string? Efin,
    Guid? SecurityCoordinatorUserId,
    string? WispAdoptedOn,
    string? WispLastReviewedOn,
    string? IncidentContactEmail,
    string? IncidentContactPhone,
    int? RetentionYears
)
{
    public OfficeComplianceDraft ToDraft() =>
        new(
            SecurityCoordinatorUserId,
            WispAdoptedOn,
            WispLastReviewedOn,
            IncidentContactEmail,
            IncidentContactPhone,
            RetentionYears
        );
}
