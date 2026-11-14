using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;

namespace TaxVision.FinancialInsights.Api.Results;

/// <summary>Códigos propios de Financial Insights; el resto cae al mapeo común de BuildingBlocks.</summary>
public static class FinancialErrorHttpMapping
{
    public static int ToFinancialHttpStatusCode(this Error error) =>
        error.Code switch
        {
            "Import.BatchNotFound" or "Import.DocumentNotFound" or "Import.CustomerNotFound" =>
                StatusCodes.Status404NotFound,
            "Import.FileAlreadyRegistered"
            or "Import.NotRetryable"
            or "Import.InferenceAlreadyAttempted"
            or "Import.InvalidTransition"
            or "Import.TenantImmutable" => StatusCodes.Status409Conflict,
            "Import.FileNotEligible" => StatusCodes.Status422UnprocessableEntity,
            "Import.FileStatusUnavailable" or "Import.EntitlementsUnavailable" => StatusCodes.Status503ServiceUnavailable,
            "Tenant.Inactive" or "Financial.ModuleUnavailable" or "Financial.PermissionDenied" =>
                StatusCodes.Status403Forbidden,
            _ => error.ToHttpStatusCode(),
        };
}
