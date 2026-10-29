using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Application.Mapping;

public static class FormCatalogUsageErrors
{
    public static readonly Error InvalidTargetKey = new(
        "FormCatalogUsage.InvalidTargetKey",
        "The target key is missing or has an invalid format."
    );
}
