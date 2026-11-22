namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// Un widget del dashboard: su id, su tamaño (<c>s</c>, <c>m</c>, <c>l</c>), si está oculto y, desde el esquema 2, su
/// alcance (<c>me</c> u <c>office</c>; null = el de siempre).
/// </summary>
public sealed record DashboardWidgetPlacement(string Id, string Size, bool Hidden, string? Scope = null);
