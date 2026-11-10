namespace TaxVision.Tenant.Domain.Profiles;

/// <summary>Un cierre tal como llega (sin validar): fechas <c>yyyy-MM-dd</c>, fin opcional y etiqueta.</summary>
public sealed record OfficeClosureDraft(string? Date, string? EndDate, string? Label, bool RepeatsYearly);
