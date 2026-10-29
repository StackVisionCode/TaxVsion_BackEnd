namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// Códigos de resultado por destino que Tasks muestra como motivo en el drawer.
public static class CustomerProfileUpdateFromFormOutcomes
{
    public const string NotAuthorized = "Customer.ProfileUpdateNotAuthorized";
    public const string CustomerNotFound = "Customer.NotFound";
    public const string TargetNotSupported = "Customer.FormTargetNotSupported";
    public const string ValueInvalid = "Customer.FormValueInvalid";
    public const string NoFiscalProfile = "Customer.NoFiscalProfile";
    public const string TaxIdentifierAlreadyExists = "Customer.TaxIdentifierAlreadyExists";
}
