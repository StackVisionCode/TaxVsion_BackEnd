using BuildingBlocks.Results;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// Alcance de un resumen del dashboard. Default = como siempre (lo visible para el actor, con sus filtros); Me = lo
/// asignado al actor; Office = toda la oficina con el recorte del actor, solo con <c>users.work.view</c>.
/// </summary>
public enum DashboardScope
{
    Default,
    Me,
    Office,
}

public static class DashboardScopes
{
    // Mismo código que el resto de Tasks: el mapeo HTTP ya lo lleva a 403.
    public static readonly Error OfficeForbidden = new(
        "Task.Forbidden",
        "The office view requires permission to view your team's work."
    );

    public static readonly Error Invalid = new("Task.Summary.InvalidScope", "scope must be 'me' or 'office'.");

    /// <summary>Vacío = <see cref="DashboardScope.Default"/>; acepta "me" u "office" sin distinguir mayúsculas.</summary>
    public static bool TryParse(string? raw, out DashboardScope scope)
    {
        scope = DashboardScope.Default;
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        if (string.Equals(raw, "me", StringComparison.OrdinalIgnoreCase))
            scope = DashboardScope.Me;
        else if (string.Equals(raw, "office", StringComparison.OrdinalIgnoreCase))
            scope = DashboardScope.Office;
        else
            return false;
        return true;
    }
}
