import type {
  UserPermissionsProjectionRepository,
  UserPermissionsProjectionSnapshot,
} from '../../application/ports/user-permissions-projection-repository.js';

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

  /**
   * Palanca del portal: es la que el administrador reconoce en el cajon de accesos del cliente
   * ("quitarle las llamadas a este cliente"). Se exige SOLO al actor CustomerPortal — equivalente
   * en Node de [HasPermissionForActor] del lado .NET: apilarla para todos dejaria al staff afuera.
   */
  PortalCallsUse: 'portal.calls.use',
} as const;

export type CommunicationPermission =
  (typeof CommunicationPermissions)[keyof typeof CommunicationPermissions];

/**
 * Sujeto minimo necesario para chequear un permiso — AuthenticatedPrincipal
 * (jwt-verifier.ts) satisface esta forma estructuralmente, sin necesidad de
 * importarlo aca (evitaria una dependencia domain -> infrastructure).
 * No incluye `permissions` (el array embebido en el JWT): RBAC Fase 7.5.9 deja
 * de confiar en ese claim — la fuente de verdad es la proyeccion local.
 */
export interface PermissionSubject {
  readonly userId: string;
  readonly tenantId: string;
  readonly actorType: string;
  readonly permissionVersion: number;
}

/**
 * Gate de modulo (Entitlements en runtime), modo LOG-ONLY — espejo del hook en
 * BuildingBlocks.Web/ActorTypeAuthorization/PermissionPolicyProvider.cs (.NET): tras conceder un
 * permiso, observa si el tenant tiene habilitado el modulo al que pertenece ese permiso y lo
 * loguea/mide SIN cambiar la decision. Opt-in: si `configureModuleGate` no se llamo (composition
 * root), el gate no corre — igual que un servicio .NET que no registra `ITenantModuleEntitlementsSource`.
 */
export type ModuleGateObserver = (subject: PermissionSubject, required: CommunicationPermission) => Promise<void>;
let moduleGateObserver: ModuleGateObserver | undefined;

/** Configura el gate de modulo (log-only). Se llama una sola vez en el composition root (main.ts). */
export function configureModuleGate(observer: ModuleGateObserver | undefined): void {
  moduleGateObserver = observer;
}

export type PermissionCheckResult =
  | { readonly allowed: true }
  | { readonly allowed: false; readonly code: string; readonly message: string };

/**
 * RBAC Fase 7.5.9 — mismo mecanismo que BuildingBlocks.Web/ActorTypeAuthorization/
 * ProjectionPermissionsSource.cs del lado .NET: ya no confiamos en el array de
 * permisos embebido en el JWT (`perm`), sino en la proyeccion local
 * `UserPermissionsProjection` (poblada por auth-consumers.ts via
 * UserRolesChangedIntegrationEvent), comparando `perm_v` contra la version de
 * la proyeccion para detectar staleness (rol cambiado despues de emitido el
 * token). Devuelve un resultado discriminado en vez de tirar una excepcion:
 * Communication no tiene un unico choke point de errores (a diferencia de la
 * ExceptionHandlingMiddleware de .NET) — hay 3 mecanismos de transporte
 * distintos (HTTP reply, socket ack, Result<T> de use case) y cada call site
 * ya sabe traducir `{code, message}` a su propio formato de respuesta.
 *
 * Cache in-memory de 30s (mismo TTL que el IMemoryCache de
 * ProjectionPermissionsSource) — evita pegarle a Prisma en cada evento de
 * socket (ej. cada SendMessage). Cacheado solo por userId: el read-model es
 * cross-tenant por diseño (ver doc-comment de UserPermissionsProjectionRepository).
 */
const PROJECTION_CACHE_TTL_MS = 30_000;
const projectionCache = new Map<
  string,
  { readonly snapshot: UserPermissionsProjectionSnapshot | null; readonly expiresAtMs: number }
>();

async function getCachedSnapshot(
  userId: string,
  projectionRepo: UserPermissionsProjectionRepository,
): Promise<UserPermissionsProjectionSnapshot | null> {
  const now = Date.now();
  const cached = projectionCache.get(userId);
  if (cached && cached.expiresAtMs > now) return cached.snapshot;
  const snapshot = await projectionRepo.findByUserId(userId);
  projectionCache.set(userId, { snapshot, expiresAtMs: now + PROJECTION_CACHE_TTL_MS });
  return snapshot;
}

export async function checkPermission(
  subject: PermissionSubject,
  required: CommunicationPermission,
  projectionRepo: UserPermissionsProjectionRepository,
): Promise<PermissionCheckResult> {
  if (isPlatformAdmin(subject.actorType)) return { allowed: true };

  const snapshot = await getCachedSnapshot(subject.userId, projectionRepo);
  if (!snapshot) {
    // Fail-closed: un usuario nunca sincronizado (o cuyo consumer todavia no
    // proceso su primer UserRolesChangedIntegrationEvent) no tiene forma de
    // probar que permisos tiene realmente — se lo trata como sin acceso, no
    // como "todo permitido". Mismo criterio que ProjectionPermissionsSource.cs.
    return { allowed: false, code: 'Auth.Forbidden', message: `Missing ${required}.` };
  }

  if (subject.permissionVersion < snapshot.permissionVersion) {
    return {
      allowed: false,
      code: 'Auth.TokenStale',
      message: 'Permissions changed since this token was issued; refresh and try again.',
    };
  }

  if (!snapshot.permissions.includes(required)) {
    return { allowed: false, code: 'Auth.Forbidden', message: `Missing ${required}.` };
  }

  // Gate de modulo LOG-ONLY — corre solo cuando el permiso YA paso por permisos reales (no para el
  // bypass de PlatformAdmin de arriba). Nunca cambia la decision ni la puede romper.
  if (moduleGateObserver) {
    try {
      await moduleGateObserver(subject, required);
    } catch {
      // log-only: jamas afectar la autorizacion por un fallo del gate.
    }
  }

  return { allowed: true };
}

/**
 * Traduce un PermissionCheckResult denegado al status HTTP correspondiente —
 * unico punto de mapeo para los 4 route files que exponen `[HasPermission]`-like
 * gates directos (evita repetir el ternario en cada uno).
 */
export function permissionCheckHttpStatus(result: Extract<PermissionCheckResult, { allowed: false }>): number {
  return result.code === 'Auth.TokenStale' ? 401 : 403;
}

/**
 * Unico punto de comparacion para el bypass de PlatformAdmin — evita repetir el string
 * literal `'PlatformAdmin'` en cada route/handler que necesita un chequeo directo (fuera de
 * `hasPermission`, ej. endpoints admin sin un `CommunicationPermission` propio).
 */
export function isPlatformAdmin(actorType: string): boolean {
  return actorType === 'PlatformAdmin';
}

/**
 * Chequea un permiso SOLO si el caller es de un actor type concreto; para los demas pasa. Es el
 * equivalente en Node de `[HasPermissionForActor(actorType, code)]` del lado .NET, y existe por el
 * mismo motivo: en un endpoint compartido entre staff y portal, exigirle a todos un permiso que solo
 * el portal tiene deja al staff afuera.
 */
export async function checkPermissionForActor(
  subject: PermissionSubject,
  actorType: string,
  required: CommunicationPermission,
  projectionRepo: UserPermissionsProjectionRepository,
): Promise<PermissionCheckResult> {
  if (subject.actorType !== actorType) return { allowed: true };
  return checkPermission(subject, required, projectionRepo);
}

/**
 * Actores "de personal" (staff): empleados/admins de la oficina y operadores de
 * plataforma. Excluye a `CustomerPortal` (clientes) y `Guest` (invitados a un
 * meeting). Se usa para gatear el directorio: sin este chequeo un cliente podia
 * enumerar TODA la plantilla y TODA la cartera de clientes del tenant por
 * autocomplete (solo estaba `authenticate`). `Service` (M2M) no llega a rutas
 * con JWT humano.
 */
export function isStaffActor(actorType: string): boolean {
  return actorType === 'TenantEmployee' || actorType === 'TenantAdmin' || actorType === 'PlatformAdmin';
}
