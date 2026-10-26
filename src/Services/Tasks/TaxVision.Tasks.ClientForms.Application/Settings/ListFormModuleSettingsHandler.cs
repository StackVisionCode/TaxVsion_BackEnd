using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Settings;

public sealed record ListFormModuleSettingsQuery;

// Oficinas con fila de settings más las que tienen planner sin fila (con sus defaults).
// Un tenant sin proyección de plan y sin fila no aparece: Tasks no tiene otra fuente de tenants.
public static class ListFormModuleSettingsHandler
{
    public static async Task<IReadOnlyList<FormModuleSettingsResponse>> Handle(
        ListFormModuleSettingsQuery _,
        IFormModuleSettingsRepository repository,
        IClientFormsTenantDirectory tenants,
        CancellationToken ct
    )
    {
        var rows = (await repository.ListAsync(ct)).ToDictionary(item => item.TenantId);
        var available = await tenants.ListAvailableTenantIdsAsync(ct);
        return rows
            .Keys.Union(available)
            .Order()
            .Select(tenantId =>
                FormModuleSettingsResponse.From(
                    tenantId,
                    available.Contains(tenantId)
                        ? ClientFormsAvailability.Available
                        : new(false, ClientFormsAvailabilityReasons.ModuleUnavailable),
                    rows.GetValueOrDefault(tenantId)
                )
            )
            .ToArray();
    }
}
