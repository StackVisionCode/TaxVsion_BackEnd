using BuildingBlocks.RateLimiting;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

// Client Forms es un módulo más: solo lo decide el plan (planner). Sin proyección, deniega.
public sealed class ClientFormsAvailabilityEvaluator(ITenantEntitlementModulesReader modulesReader)
    : IClientFormsAvailabilityEvaluator
{
    public async Task<ClientFormsAvailability> EvaluateAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return new(false, ClientFormsAvailabilityReasons.ModuleUnavailable);

        var modules = await modulesReader.GetEnabledModulesAsync(tenantId, ct);
        return modules?.Contains(ClientFormsModule.RequiredModule, StringComparer.OrdinalIgnoreCase) == true
            ? ClientFormsAvailability.Available
            : new(false, ClientFormsAvailabilityReasons.ModuleUnavailable);
    }
}
