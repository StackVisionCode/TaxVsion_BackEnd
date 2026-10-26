using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

// Sin fila de settings, la oficina usa los defaults de FormModuleCapabilities.
public sealed class ClientFormsPublicCapabilityEvaluator(
    IClientFormsAvailabilityEvaluator availability,
    IFormModuleSettingsRepository settingsRepository,
    IFormIntakeBotProtection botProtection
) : IClientFormsPublicCapabilityEvaluator
{
    public async Task<bool> IsExternalParticipantAvailableAsync(Guid tenantId, CancellationToken ct = default)
    {
        var module = await availability.EvaluateAsync(tenantId, ct);
        if (!module.Enabled)
            return false;
        var settings = await settingsRepository.GetAsync(tenantId, ct);
        return FormModuleCapabilities.Of(settings).ExternalParticipantsEnabled;
    }

    public async Task<bool> IsIntakeLinkAvailableAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!botProtection.IsReady)
            return false;
        var module = await availability.EvaluateAsync(tenantId, ct);
        if (!module.Enabled)
            return false;
        var settings = await settingsRepository.GetAsync(tenantId, ct);
        return FormModuleCapabilities.Of(settings).IntakeLinksEnabled;
    }
}
