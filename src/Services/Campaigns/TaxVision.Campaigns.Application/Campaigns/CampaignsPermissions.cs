namespace TaxVision.Campaigns.Application.Campaigns;

/// <summary>
/// Constantes de permiso usadas por <c>[HasPermission(...)]</c>. El permiso <c>campaigns.manage</c>
/// YA está cableado centralmente en Auth (<c>PermissionCatalog.cs:39</c>, módulo <c>campaigns</c>,
/// MinPlanTier Pro) — aquí solo se referencia el string (no se re-agrega).
/// </summary>
public static class CampaignsPermissions
{
    public const string Manage = "campaigns.manage";
}
