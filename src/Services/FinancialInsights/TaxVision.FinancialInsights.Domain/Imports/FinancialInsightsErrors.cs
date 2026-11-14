using BuildingBlocks.Results;

namespace TaxVision.FinancialInsights.Domain.Imports;

/// <summary>Errores de Financial Insights. Los códigos Import.* ya los lee el CRM; no cambiarlos.</summary>
public static class FinancialInsightsErrors
{
    public static readonly Error InvalidBatch = new("Import.InvalidBatch", "Provide 1 to 20 distinct file IDs.");
    public static readonly Error TenantRequired = new("Import.TenantRequired", "The tenant is required.");
    public static readonly Error TenantImmutable = new("Import.TenantImmutable", "The tenant cannot change.");
    public static readonly Error CustomerRequired = new("Import.CustomerRequired", "The client is required.");
    public static readonly Error CreatorRequired = new("Import.CreatorRequired", "The creator is required.");
    public static readonly Error DocumentRequired = new("Import.DocumentRequired", "The document is required.");
    public static readonly Error FailureReasonRequired = new(
        "Import.FailureReasonRequired",
        "A failure reason is required."
    );
    public static readonly Error BatchNotFound = new("Import.BatchNotFound", "The import is unavailable.");
    public static readonly Error DocumentNotFound = new("Import.DocumentNotFound", "The document is unavailable.");
    public static readonly Error CustomerNotFound = new("Import.CustomerNotFound", "The client is unavailable.");
    public static readonly Error InvalidTransition = new(
        "Import.InvalidTransition",
        "The document is not in a state that allows this change."
    );
    public static readonly Error NotRetryable = new("Import.NotRetryable", "Only failed documents can be retried.");
    public static readonly Error InferenceAlreadyAttempted = new(
        "Import.InferenceAlreadyAttempted",
        "This document cannot be retried after an inference attempt."
    );
    public static readonly Error FileAlreadyRegistered = new(
        "Import.FileAlreadyRegistered",
        "A file is already registered in another import."
    );
    public static readonly Error FileStatusUnavailable = new(
        "Import.FileStatusUnavailable",
        "File status is temporarily unavailable."
    );
    public static readonly Error EntitlementsUnavailable = new(
        "Import.EntitlementsUnavailable",
        "The office plan is not available yet. Try again shortly."
    );
    public static readonly Error TenantInactive = new("Tenant.Inactive", "The office is not active.");
    public static readonly Error ModuleUnavailable = new(
        "Financial.ModuleUnavailable",
        "Financial Insights is not enabled for this account."
    );
    public static readonly Error PermissionDenied = new(
        "Financial.PermissionDenied",
        "This account cannot upload files for import."
    );

    public static readonly Error MovementInvalidAmount = new("Import.Movement.InvalidAmount", "Invalid amount.");
    public static readonly Error MovementInvalidCurrency = new("Import.Movement.InvalidCurrency", "Invalid currency.");
    public static readonly Error MovementInvalidDirection = new(
        "Import.Movement.InvalidDirection",
        "Direction must be credit or debit."
    );
    public static readonly Error MovementDescriptionRequired = new(
        "Import.Movement.DescriptionRequired",
        "A description is required."
    );
    public static readonly Error MovementCategoryRequired = new(
        "Import.Movement.CategoryRequired",
        "A suggested category is required."
    );
    public static readonly Error MovementInvalidSourcePage = new(
        "Import.Movement.InvalidSourcePage",
        "Invalid source page."
    );
    public static readonly Error ReservationModelRequired = new(
        "Import.Reservation.ModelRequired",
        "The model is required."
    );
    public static readonly Error ReservationAlreadyCompleted = new(
        "Import.Reservation.AlreadyCompleted",
        "Usage was already recorded."
    );

    public static Error FileNotEligible(Guid fileId) =>
        new(
            "Import.FileNotEligible",
            $"File {fileId:D} is not eligible. Confirm its scan status, type, owner and 25 MB limit."
        );
}
