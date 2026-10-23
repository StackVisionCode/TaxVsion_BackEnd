namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// Códigos que Tasks interpreta para decidir si un rechazo admite reintento manual.
public static class CustomerCreationFromFormOutcomes
{
    public const string ClientFormsSourceService = "tasks.client_forms";

    public const string DuplicateFound = "Customer.DuplicateFound";
    public const string CreatorNotInDirectory = "Customer.CreatorNotInDirectory";
    public const string CreatorNotEligible = "Customer.CreatorNotEligible";
    public const string FormTargetNotSupported = "Customer.FormTargetNotSupported";
    public const string FormValueInvalid = "Customer.FormValueInvalid";

    public const string AddressNotSavedWarning = "address_not_saved";
}
