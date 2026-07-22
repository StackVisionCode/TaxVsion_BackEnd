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
  // Fase Backend 9 — reactions/pin/forward/search. Todos los participantes de
  // una conversation pueden reaccionar y buscar (misma politica que ChatReply);
  // pin en Direct = ambos, pin en Group/Meeting = quien tenga ChatModerate.
  // Estos son alias semanticos — el enforcement real vive en cada use case.
  ChatReact: 'communication.chat.react',
  ChatPin: 'communication.chat.pin',
  ChatForward: 'communication.chat.forward',
  ChatSearch: 'communication.chat.search',

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
 * Verifica si un principal tiene un permiso concreto. Solo PlatformAdmin pasa
 * siempre — TenantAdmin depende del claim "perm" real (Auth se lo otorga por
 * defecto vía PermissionCatalog.SystemRoleDefaults al emitir el JWT, así que
 * en la práctica lo sigue teniendo todo, pero ya no por un bypass de rol).
 * Corregido tras encontrar el mismo bypass en las 8 policies .NET del backend
 * — dejaba pasar cualquier permiso, incluso uno 100% exclusivo de plataforma,
 * a cualquier TenantAdmin (ver signature.constraints.manage en Auth).
 * NUNCA leer roles desde el request body/query — solo desde el token verificado.
 */
export function hasPermission(
  actorType: string,
  permissions: readonly string[],
  required: CommunicationPermission,
): boolean {
  if (isPlatformAdmin(actorType)) return true;
  return permissions.includes(required);
}

/**
 * Unico punto de comparacion para el bypass de PlatformAdmin — evita repetir el string
 * literal `'PlatformAdmin'` en cada route/handler que necesita un chequeo directo (fuera de
 * `hasPermission`, ej. endpoints admin sin un `CommunicationPermission` propio).
 */
export function isPlatformAdmin(actorType: string): boolean {
  return actorType === 'PlatformAdmin';
}
