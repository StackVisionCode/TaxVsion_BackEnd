using TaxVision.Tenant.Domain.Profiles;

namespace TaxVision.Tenant.Api.Contracts;

/// <summary>Un cierre: fechas <c>yyyy-MM-dd</c>, fin opcional, etiqueta y si se repite cada año.</summary>
public sealed record OfficeClosureRequest(string? Date, string? EndDate, string? Label, bool RepeatsYearly)
{
    public OfficeClosureDraft ToDraft() => new(Date, EndDate, Label, RepeatsYearly);
}
