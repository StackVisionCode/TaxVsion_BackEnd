/**
 * Espejo estatico del catalogo de permisos definido en Auth
 * (BuildingBlocks.Authorization.CommunicationPermissions.cs).
 * Cualquier cambio en Auth debe reflejarse aqui. Es la unica constante que
 * conocemos del contrato Auth ↔ Communication.
 */
export const CommunicationPermissions = {
  ChatStart: 'communication.chat.start',
  ChatReply: 'communication.chat.reply',
  ChatModerate: 'communication.chat.moderate',

  SupportOpen: 'communication.support.open',
  SupportAgent: 'communication.support.agent',

  CallStart: 'communication.call.start',
  VideoCallStart: 'communication.videocall.start',
  CallRecord: 'communication.call.record',

  MeetingCreate: 'communication.meeting.create',
  MeetingJoin: 'communication.meeting.join',
  MeetingHost: 'communication.meeting.host',
  MeetingRecord: 'communication.meeting.record',

  ScreenshotCreate: 'communication.screenshot.create',

  GroupCreate: 'communication.group.create',
  GroupManageMembers: 'communication.group.manage_members',

  NotificationRead: 'communication.notification.read',

  SettingsManage: 'communication.settings.manage',
  AnalyticsRead: 'communication.analytics.read',
} as const;

export type CommunicationPermission =
  (typeof CommunicationPermissions)[keyof typeof CommunicationPermissions];

/**
 * Verifica si un principal tiene un permiso concreto. TenantAdmin/PlatformAdmin
 * pasan siempre — mismo comportamiento que las policies .NET del backend.
 * NUNCA leer roles desde el request body/query — solo desde el token verificado.
 */
export function hasPermission(
  actorType: string,
  permissions: readonly string[],
  required: CommunicationPermission,
): boolean {
  if (actorType === 'TenantAdmin' || actorType === 'PlatformAdmin') return true;
  return permissions.includes(required);
}
