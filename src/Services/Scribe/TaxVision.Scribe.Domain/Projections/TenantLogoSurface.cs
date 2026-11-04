namespace TaxVision.Scribe.Domain.Projections;

/// <summary>Superficie del logo de la oficina: lo que ve el equipo (CRM) o el cliente (Portal).</summary>
public enum TenantLogoSurface
{
    Crm,
    Portal,
}

public static class TenantLogoSurfaces
{
    private static readonly TenantLogoSurface[] StaffPreference = [TenantLogoSurface.Crm];
    private static readonly TenantLogoSurface[] ClientPreference = [TenantLogoSurface.Portal, TenantLogoSurface.Crm];

    /// <summary>Orden en que se busca el logo para una audiencia: el cliente usa el del portal y cae al del CRM.</summary>
    public static IReadOnlyList<TenantLogoSurface> PreferenceFor(RenderAudience audience) =>
        audience == RenderAudience.Client ? ClientPreference : StaffPreference;

    /// <summary>Lee la superficie del contrato de Tenant. Un valor desconocido no se proyecta.</summary>
    public static bool TryParse(string? value, out TenantLogoSurface surface) =>
        Enum.TryParse(value, ignoreCase: true, out surface) && Enum.IsDefined(surface);
}
