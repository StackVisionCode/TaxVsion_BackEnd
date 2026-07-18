namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio Scribe (templates/layouts de correo, event mappings, render).
/// Mismo patrón que <see cref="NotificationPermissions"/>/<see cref="PostmasterPermissions"/>: claves
/// punteadas en minúsculas usadas como claim "perm" en el JWT y como policy en los endpoints.
/// </summary>
public static class ScribePermissions
{
    public const string TemplatesRead = "scribe.templates.read";
    public const string TemplatesWrite = "scribe.templates.write";
    public const string LayoutsRead = "scribe.layouts.read";
    public const string LayoutsWrite = "scribe.layouts.write";
    public const string EventMappingsRead = "scribe.event_mappings.read";
    public const string EventMappingsWrite = "scribe.event_mappings.write";
    public const string CampaignsRead = "scribe.campaigns.read";
    public const string CampaignsWrite = "scribe.campaigns.write";

    /// <summary>
    /// M2M — Notification (u otros servicios) invocando "POST /scribe/render" vía token de
    /// servicio. Gateado por el mismo [HasPermission(...)] que los endpoints humanos (no
    /// ServiceOnly/actor_type) — el token de servicio debe llevar este código en su Permissions
    /// configurados (ServiceAuth:Clients, Auth) para que el claim "perm" llegue al JWT. El gRPC
    /// que existía en paralelo (TemplateRenderGrpcService) se retiró en la Fase 8 del hardening
    /// (ADR-0003) — este permiso nunca lo gateó, siempre fue exclusivo del endpoint HTTP.
    /// </summary>
    public const string Render = "scribe.render";
}
