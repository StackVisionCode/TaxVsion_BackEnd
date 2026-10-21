using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

public sealed class ClientFormsAvailabilityEvaluator(
    IOptions<ClientFormsOptions> options,
    IFormModuleSettingsRepository settingsRepository,
    ITenantEntitlementModulesReader modulesReader
) : IClientFormsAvailabilityEvaluator
{
    private const string RequiredModule = "planner";

    public async Task<ClientFormsAvailability> EvaluateAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
            return new(false, ClientFormsAvailabilityReasons.NotAvailable);

        var settings = tenantId == Guid.Empty ? null : await settingsRepository.GetAsync(tenantId, ct);
        if (settings is not { Enabled: true })
            return new(false, ClientFormsAvailabilityReasons.NotEnabled);

        var modules = await modulesReader.GetEnabledModulesAsync(tenantId, ct);
        return modules?.Contains(RequiredModule, StringComparer.OrdinalIgnoreCase) == true
            ? ClientFormsAvailability.Available
            : new(false, ClientFormsAvailabilityReasons.ModuleUnavailable);
    }
}
