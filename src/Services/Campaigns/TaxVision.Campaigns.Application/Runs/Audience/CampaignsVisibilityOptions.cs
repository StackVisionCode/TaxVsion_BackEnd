namespace TaxVision.Campaigns.Application.Runs.Audience;

// Flag de visibilidad por asignación (default false = una campaña con audiencia "Clients" alcanza a TODOS
// los clientes activos, comportamiento actual, hasta sembrar la proyección con la reconciliación). Espejo de
// Customers:AssignmentVisibility. Con el flag ON, un empleado que NO ve todo (customers.view_all) solo puede
// enviar a los clientes ASIGNADOS a él; los runs agendados (actor de sistema) no se restringen.
public sealed class CampaignsVisibilityOptions
{
    public const string SectionName = "Campaigns:AssignmentVisibility";
    public bool Enabled { get; set; }
}
