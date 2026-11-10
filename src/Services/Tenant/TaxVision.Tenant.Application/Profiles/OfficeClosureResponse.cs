namespace TaxVision.Tenant.Application.Profiles;

/// <summary>Un cierre de la oficina: fechas <c>yyyy-MM-dd</c> (fin opcional e incluido).</summary>
public sealed record OfficeClosureResponse(string Date, string? EndDate, string Label, bool RepeatsYearly);
