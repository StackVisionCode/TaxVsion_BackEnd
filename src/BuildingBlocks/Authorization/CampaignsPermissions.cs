namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del servicio Campaigns. Antes había uno solo, <c>campaigns.manage</c>, que cubría desde
/// ver la lista hasta disparar un envío masivo a toda la cartera: delegar "que preparen la campaña"
/// significaba delegar también "que la manden". Los cuatro separan las tres cosas que de verdad se
/// delegan por separado, más la identidad del remitente.
/// </summary>
public static class CampaignsPermissions
{
    /// <summary>Ver campañas, contactos, listas, programaciones y corridas.</summary>
    public const string View = "campaigns.view";

    /// <summary>Crear y editar campañas, contactos y listas. No manda nada.</summary>
    public const string Manage = "campaigns.manage";

    /// <summary>Disparar un envío o programarlo. Es la acción irreversible del servicio.</summary>
    public const string Send = "campaigns.send";

    /// <summary>Administrar los perfiles de remitente (de quién sale el correo de la oficina).</summary>
    public const string SendersManage = "campaigns.senders.manage";
}
