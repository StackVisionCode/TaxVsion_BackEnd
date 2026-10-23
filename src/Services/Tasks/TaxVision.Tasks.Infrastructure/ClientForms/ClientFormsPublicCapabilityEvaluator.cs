using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

internal sealed class ClientFormsPublicCapabilityEvaluator(
    IClientFormsAvailabilityEvaluator availability,
    IFormModuleSettingsRepository settingsRepository,
    IFormIntakeBotProtection botProtection
) : IClientFormsPublicCapabilityEvaluator
{
    public async Task<bool> IsExternalParticipantAvailableAsync(
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        var module = await availability.EvaluateAsync(tenantId, ct);
        if (!module.Enabled)
            return false;
        var settings = await settingsRepository.GetAsync(tenantId, ct);
        return settings is { ExternalParticipantsEnabled: true };
    }

    public async Task<bool> IsIntakeLinkAvailableAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!botProtection.IsReady)
            return false;
        var module = await availability.EvaluateAsync(tenantId, ct);
        if (!module.Enabled)
            return false;
        var settings = await settingsRepository.GetAsync(tenantId, ct);
        return settings is { IntakeLinksEnabled: true };
    }
}
