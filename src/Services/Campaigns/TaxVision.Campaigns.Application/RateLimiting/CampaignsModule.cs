namespace TaxVision.Campaigns.Application.RateLimiting;

/// <summary>Código del módulo comercial de Campaigns (ver <c>PermissionModuleMap</c>).</summary>
public static class CampaignsModule
{
    public const string Code = "campaigns";

    // Cómo aparece en EnabledModulesJson, para filtrar en la consulta (la intercalación de SQL Server ignora mayúsculas).
    public const string JsonToken = "\"" + Code + "\"";
}
