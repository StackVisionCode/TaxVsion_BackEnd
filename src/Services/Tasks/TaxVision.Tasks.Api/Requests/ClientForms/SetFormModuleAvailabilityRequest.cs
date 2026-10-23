namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record SetFormModuleAvailabilityRequest(
    bool Enabled,
    string? Note,
    bool? ExternalParticipantsEnabled = null,
    bool? IntakeLinksEnabled = null,
    bool? AutoCustomerCreationEnabled = null,
    bool? SignatureStepEnabled = null,
    bool? PushNotificationsEnabled = null
);
