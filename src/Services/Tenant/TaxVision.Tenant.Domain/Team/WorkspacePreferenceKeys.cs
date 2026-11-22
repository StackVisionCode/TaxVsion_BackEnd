namespace TaxVision.Tenant.Domain.Team;

/// <summary>Lista blanca de claves de preferencias. Sin puntos, para que ningún proxy la tome por un archivo.</summary>
public static class WorkspacePreferenceKeys
{
    public const string CrmDashboardLayout = "crm-dashboard-layout";

    public const int MaxLength = 64;

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { CrmDashboardLayout };

    public static bool IsKnown(string? key) => key is not null && Known.Contains(key);
}
