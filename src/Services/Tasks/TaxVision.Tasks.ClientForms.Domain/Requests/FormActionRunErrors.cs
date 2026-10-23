using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

public static class FormActionRunErrors
{
    // Rechazos de Customer que no admiten reintento: el duplicado se resuelve vinculando.
    public const string DuplicateFoundCode = "Customer.DuplicateFound";
    public const string ConditionNotMetCode = "FormAction.ConditionNotMet";
    public const string NotSupportedCode = "FormAction.NotSupported";
    public const string PayloadUnavailableCode = "FormAction.PayloadUnavailable";

    public static readonly Error NotFound = new("FormActionRun.NotFound", "The form action run was not found.");
    public static readonly Error NotRetryable = new(
        "FormActionRun.NotRetryable",
        "This action cannot be retried in its current state."
    );
    public static readonly Error NotAvailable = new(
        "FormActionRun.NotAvailable",
        "This action is not available for the request."
    );
    public static readonly Error Invalid = new("FormActionRun.Invalid", "The form action run is invalid.");
}
