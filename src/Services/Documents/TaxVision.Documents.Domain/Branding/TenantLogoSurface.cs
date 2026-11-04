namespace TaxVision.Documents.Domain.Branding;

/// <summary>Superficie del logo de la oficina: lo que ve el equipo (CRM) o el cliente (Portal).</summary>
public enum TenantLogoSurface
{
    Crm,
    Portal,
}

/// <summary>Quién lee el documento. El cliente ve el logo del portal (con el del CRM de respaldo).</summary>
public enum LogoAudience
{
    Staff,
    Client,
}

public static class TenantLogoSurfaces
{
    private static readonly TenantLogoSurface[] StaffPreference = [TenantLogoSurface.Crm];
    private static readonly TenantLogoSurface[] ClientPreference = [TenantLogoSurface.Portal, TenantLogoSurface.Crm];

    /// <summary>Orden en que se busca el logo para una audiencia.</summary>
    public static IReadOnlyList<TenantLogoSurface> PreferenceFor(LogoAudience audience) =>
        audience == LogoAudience.Client ? ClientPreference : StaffPreference;

    /// <summary>Lee la superficie del contrato de Tenant. Un valor desconocido no se proyecta.</summary>
    public static bool TryParse(string? value, out TenantLogoSurface surface) =>
        Enum.TryParse(value, ignoreCase: true, out surface) && Enum.IsDefined(surface);
}
