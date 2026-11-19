using BuildingBlocks.Contracts.Work;

namespace TaxVision.Tenant.Application.Team;

/// <summary>
/// Registro de proveedores del Work Profile: el CRM pide a cada uno el trabajo de una persona. <c>Service</c> es el
/// servicio dueño (carpeta en src/Services) y <c>Module</c> el módulo de plan que lo habilita, para ocultar el bloque
/// si la oficina no lo tiene. Un fitness test exige que ambos paths existan en el servicio.
/// </summary>
public static class TeamWorkProviders
{
    public static IReadOnlyList<WorkProviderDescriptor> All { get; } =
    [
        new("tasks", "planner", "/tasks/work/{userId}", "/tasks/work/summary"),
        new("calendar", "planner", "/calendar/work/{userId}", "/calendar/work/summary"),
        new("signature", "signatures", "/signature/work/{userId}", "/signature/work/summary"),
        new("customer", "customers", "/customers/work/{userId}", "/customers/work/summary"),
        new("communication", "comms", "/communication/work/{userId}", "/communication/work/summary"),
    ];
}
