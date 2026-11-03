using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.Compliance;

/// <summary>Errores del cumplimiento de la oficina (solo administradores).</summary>
public static class OfficeComplianceErrors
{
    public const string IncidentContactEmailCode = "Tenant.Compliance.IncidentContactEmail";
    public const string IncidentContactPhoneCode = "Tenant.Compliance.IncidentContactPhone";
    public const string DateFormat = "yyyy-MM-dd";

    public static readonly Error EfinNotSet = new("Tenant.Compliance.EfinNotSet", "The office has no EFIN on file.");

    public static readonly Error WispAdoptedOn = new(
        "Tenant.Compliance.WispAdoptedOn",
        "The WISP adoption date must be a past date in yyyy-MM-dd format."
    );

    public static readonly Error WispLastReviewedOn = new(
        "Tenant.Compliance.WispLastReviewedOn",
        "The WISP review date must be a past date in yyyy-MM-dd format, on or after the adoption date."
    );

    public static readonly Error SecurityCoordinator = new(
        "Tenant.Compliance.SecurityCoordinator",
        "The security coordinator must be an active user of this office."
    );
}
