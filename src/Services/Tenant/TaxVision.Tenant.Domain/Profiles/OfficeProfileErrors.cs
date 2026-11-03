using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.Profiles;

/// <summary>Errores por campo del perfil que no pertenecen a un value object propio del perfil.</summary>
public static class OfficeProfileErrors
{
    public const string PhoneCode = "Tenant.Profile.Phone";
    public const string EmailCode = "Tenant.Profile.Email";

    public static readonly Error EntityType = new(
        "Tenant.Profile.EntityType",
        "Entity type must be one of: SoleProprietor, Llc, Partnership, SCorp, CCorp, Nonprofit, Other."
    );

    public static readonly Error Website = new(
        "Tenant.Profile.Website",
        "Enter a valid website, like yourfirm.com or https://yourfirm.com."
    );

    public static readonly Error BookingUrl = new(
        "Tenant.Profile.BookingUrl",
        "Enter a valid booking link, like https://calendly.com/yourfirm."
    );

    public static readonly Error ReviewUrl = new(
        "Tenant.Profile.ReviewUrl",
        "Enter a valid review link, like https://g.page/r/yourfirm/review."
    );
}
