using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.CustomerAccess;

public static class CustomerAccessErrors
{
    public static readonly Error InvalidReason = new(
        "CustomerAccessRule.InvalidReason",
        "The reason exceeds the 500-character limit."
    );
    public static readonly Error InvalidIdentifiers = new(
        "CustomerAccessRule.InvalidIdentifiers",
        "Tenant, customer and resource identifiers are required."
    );
    public static readonly Error Forbidden = new(
        "CustomerAccessRule.Forbidden",
        "The actor cannot manage access rules for this customer."
    );
    public static readonly Error ResourceNotFound = new(
        "CustomerAccessRule.ResourceNotFound",
        "The folder or file referenced by the rule does not exist."
    );
    public static readonly Error ResourceNotOwnedByCustomer = new(
        "CustomerAccessRule.ResourceNotOwnedByCustomer",
        "The folder or file is not owned by the specified customer."
    );
    public static readonly Error ResourceTypeNotSupported = new(
        "CustomerAccessRule.ResourceTypeNotSupported",
        "Only Folder and File resources are accepted by the admin endpoints."
    );
}
