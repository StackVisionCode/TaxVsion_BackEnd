namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio Communication (chat, calls, meetings, notifs realtime,
/// support cross-tenant). Mismo patrón que <see cref="SignaturePermissions"/> y
/// <see cref="NotificationPermissions"/>: claves punteadas en minúsculas usadas como
/// claim <c>perm</c> en el JWT y verificadas por Communication (Node.js/TS) al
/// autorizar cada acción HTTP o socket.
///
/// <para>
/// Los admins (TenantAdmin/PlatformAdmin) pasan siempre; el resto necesita el claim
/// específico. La verificación vive en Communication vía JWKS pull desde Auth
/// (RS256) — Communication nunca comparte secreto HS256.
/// </para>
/// </summary>
public static class CommunicationPermissions
{
    // Chat directo (cliente ↔ empleado, empleado ↔ empleado si el tenant lo habilita)
    public const string ChatStart = "communication.chat.start";
    public const string ChatReply = "communication.chat.reply";
    public const string ChatModerate = "communication.chat.moderate";

    // Support chat cross-tenant (PlatformTenant agents ↔ customer tenants)
    public const string SupportOpen = "communication.support.open";
    public const string SupportAgent = "communication.support.agent";

    // Calls 1:1 (audio y video)
    public const string CallStart = "communication.call.start";
    public const string VideoCallStart = "communication.videocall.start";
    public const string CallRecord = "communication.call.record";

    // Meetings (multi-party con waiting room, host controls)
    public const string MeetingCreate = "communication.meeting.create";
    public const string MeetingJoin = "communication.meeting.join";
    public const string MeetingHost = "communication.meeting.host";
    public const string MeetingRecord = "communication.meeting.record";

    // Screenshots / voice / video attachments desde el chat
    public const string ScreenshotCreate = "communication.screenshot.create";

    // Grupos internos por tenant (feature apagada por default)
    public const string GroupCreate = "communication.group.create";
    public const string GroupManageMembers = "communication.group.manage_members";

    // Notificaciones in-app realtime
    public const string NotificationRead = "communication.notification.read";

    // Configuración por tenant (canales habilitados, retención, límites)
    public const string SettingsManage = "communication.settings.manage";

    // Analytics del propio microservicio (mismos usuarios que ven request.read en otros)
    public const string AnalyticsRead = "communication.analytics.read";
}
