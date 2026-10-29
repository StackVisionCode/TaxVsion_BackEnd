namespace TaxVision.Tasks.Api.Requests.ClientForms;

// Client Forms depende solo del módulo planner: Enabled true u omitido se ignora; false responde 400
// FormModuleSettings.EnabledNotSupported. Lo omitido (null) se conserva, Note "" borra la nota y una
// oficina nueva arranca con los defaults.
public sealed record SetFormModuleAvailabilityRequest(
    bool? Enabled = null,
    string? Note = null,
    bool? ExternalParticipantsEnabled = null,
    bool? IntakeLinksEnabled = null,
    bool? AutoCustomerCreationEnabled = null,
    bool? SignatureStepEnabled = null,
    bool? PushNotificationsEnabled = null
);
