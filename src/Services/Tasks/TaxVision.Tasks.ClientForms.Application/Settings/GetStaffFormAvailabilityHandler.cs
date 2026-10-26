using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.ClientForms.Application.Settings;

public sealed record GetStaffFormAvailabilityQuery(Guid TenantId);

// Solo sub-flags efectivos: ni nota ni quién los cambió.
public sealed record FormCapabilitiesResponse(
    bool ExternalParticipantsEnabled,
    bool IntakeLinksEnabled,
    bool AutoCustomerCreationEnabled,
    bool SignatureStepEnabled,
    bool PushNotificationsEnabled
)
{
    public static FormCapabilitiesResponse From(FormModuleCapabilities capabilities) =>
        new(
            capabilities.ExternalParticipantsEnabled,
            capabilities.IntakeLinksEnabled,
            capabilities.AutoCustomerCreationEnabled,
            capabilities.SignatureStepEnabled,
            capabilities.PushNotificationsEnabled
        );
}

// Capabilities null cuando el módulo no está disponible (sin planner).
public sealed record StaffFormAvailabilityResponse(
    bool Enabled,
    string? Reason,
    FormCapabilitiesResponse? Capabilities
);

public static class GetStaffFormAvailabilityHandler
{
    public static async Task<StaffFormAvailabilityResponse> Handle(
        GetStaffFormAvailabilityQuery query,
        IClientFormsAvailabilityEvaluator availability,
        IFormModuleSettingsRepository settings,
        CancellationToken ct
    )
    {
        var module = await availability.EvaluateAsync(query.TenantId, ct);
        if (!module.Enabled)
            return new(false, module.Reason, null);
        var row = await settings.GetAsync(query.TenantId, ct);
        return new(true, null, FormCapabilitiesResponse.From(FormModuleCapabilities.Of(row)));
    }
}
