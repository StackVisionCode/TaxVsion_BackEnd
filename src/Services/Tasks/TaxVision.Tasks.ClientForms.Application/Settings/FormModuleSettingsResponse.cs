using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.ClientForms.Application.Settings;

public sealed record FormModuleSettingsResponse(
    Guid TenantId,
    bool Enabled,
    DateTime? EnabledAtUtc,
    Guid? EnabledByUserId,
    string? Note,
    bool ExternalParticipantsEnabled,
    bool IntakeLinksEnabled,
    bool AutoCustomerCreationEnabled,
    bool SignatureStepEnabled,
    bool PushNotificationsEnabled
)
{
    public static FormModuleSettingsResponse From(FormModuleSettings settings) =>
        new(
            settings.TenantId,
            settings.Enabled,
            settings.EnabledAtUtc,
            settings.EnabledByUserId,
            settings.Note,
            settings.ExternalParticipantsEnabled,
            settings.IntakeLinksEnabled,
            settings.AutoCustomerCreationEnabled,
            settings.SignatureStepEnabled,
            settings.PushNotificationsEnabled
        );
}
