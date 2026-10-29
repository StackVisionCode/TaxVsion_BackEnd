using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.ClientForms.Application.Settings;

/// <summary>
/// Vista de plataforma de una oficina. <c>Enabled</c>/<c>Reason</c> son la disponibilidad real (módulo
/// planner); los sub-flags son los efectivos (defaults si <c>Configured</c> es false, es decir, sin fila).
/// </summary>
public sealed record FormModuleSettingsResponse(
    Guid TenantId,
    bool Enabled,
    string? Reason,
    bool Configured,
    Guid? EnabledByUserId,
    string? Note,
    bool ExternalParticipantsEnabled,
    bool IntakeLinksEnabled,
    bool AutoCustomerCreationEnabled,
    bool SignatureStepEnabled,
    bool PushNotificationsEnabled
)
{
    public static FormModuleSettingsResponse From(
        Guid tenantId,
        ClientFormsAvailability availability,
        FormModuleSettings? settings
    )
    {
        var capabilities = FormModuleCapabilities.Of(settings);
        return new(
            tenantId,
            availability.Enabled,
            availability.Reason,
            settings is not null,
            settings?.EnabledByUserId,
            settings?.Note,
            capabilities.ExternalParticipantsEnabled,
            capabilities.IntakeLinksEnabled,
            capabilities.AutoCustomerCreationEnabled,
            capabilities.SignatureStepEnabled,
            capabilities.PushNotificationsEnabled
        );
    }
}
