using BuildingBlocks.Results;

namespace TaxVision.Tenant.Application.Profiles.Abstractions;

public static class BillingIssuerProfileErrors
{
    public static readonly Error Unavailable = new(
        "Tenant.Profile.BillingUnavailable",
        "Billing could not be reached to read the issuer profiles. Profiles already imported are kept; run the import again later."
    );
}
