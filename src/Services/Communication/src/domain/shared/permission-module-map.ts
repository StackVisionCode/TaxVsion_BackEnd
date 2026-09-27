/**
 * Espejo de BuildingBlocks.Authorization.PermissionModuleMap (.NET): mapea un codigo de permiso a
 * su modulo comercial (los `module.*` que Subscription habilita por plan). Un permiso SIN modulo es
 * siempre efectivo (`null`) — el gate nunca lo bloquea.
 *
 * Communication solo emite permisos `communication.*` (-> modulo `comms`), pero se porta el mapa
 * completo para no divergir del lado .NET, igual que `CommunicationPermissions` es el espejo del
 * catalogo de Auth. Funcion pura, sin estado ni I/O.
 */
const PREFIX_TO_MODULE: ReadonlyArray<readonly [string, string]> = [
  ['customers.', 'customers'],
  ['signature.', 'signatures'],
  ['documents.', 'documents'],
  ['cloudstorage.', 'documents'],
  ['scribe.', 'documents'],
  ['calendar.', 'planner'],
  ['reminders.', 'planner'],
  ['tasks.', 'planner'],
  ['notes.', 'planner'],
  ['correspondence.', 'email'],
  ['connectors.', 'email'],
  ['postmaster.', 'email'],
  ['email.', 'email'],
  // ⚠️ `communication.meeting.` va ANTES que `communication.` y es el unico par de prefijos que se
  // solapa: gana el primero que calza. Las reuniones se venden aparte (modulo `meetings`, Pro y
  // Enterprise) mientras que chat, llamadas y video van en todos los planes. Invertirlos deja las
  // reuniones dentro de `comms` en silencio y las regala en Starter.
  ['communication.meeting.', 'meetings'],
  ['communication.', 'comms'],
  ['comms.', 'comms'],
  ['campaigns.', 'campaigns'],
  ['reports.', 'reports'],
];

/**
 * A6/A5.2 — permisos que caen bajo un prefijo con modulo pero que el gate NO debe exigir. Espejo
 * exacto de `PermissionModuleMap.Exempt` (.NET); si las dos listas divergen, un tenant recibe 403 en
 * Communication y 200 en el resto, o al contrario.
 *
 * Los tres viven en este microservicio —y por eso el prefijo `communication.` los llevaria a
 * `comms`— pero ninguno ES la feature que se vende con ese modulo, y `SystemRoleDefaults` los otorga
 * a CUALQUIER tenant:
 *
 * - `notification.read`: las notificaciones in-app son transversales (avisan de documentos, firmas y
 *   tareas, que el tenant si paga). Gatearlas por `comms` apagaria la campanita de TODO lo demas.
 * - `support.open`: abrir soporte hacia la plataforma. Es el camino para SALIR de un problema de
 *   plan o de pago; exigir el modulo dejaria sin voz justo al tenant que peor esta.
 * - `support.agent`: el otro lado del mismo chat, que atiende el tenant de la plataforma. No hay
 *   plan que lo habilite porque no se vende.
 */
const EXEMPT_PERMISSIONS: ReadonlySet<string> = new Set([
  'communication.notification.read',
  'communication.support.open',
  'communication.support.agent',
]);

/** Los permisos exentos del gate, para los tests y para documentar la decision. */
export const exemptPermissions: ReadonlySet<string> = EXEMPT_PERMISSIONS;

/** El modulo comercial del permiso, o `null` si no esta gateado por modulo (siempre efectivo). */
export function moduleFor(permissionCode: string): string | null {
  if (!permissionCode) return null;
  if (EXEMPT_PERMISSIONS.has(permissionCode)) return null;
  for (const [prefix, module] of PREFIX_TO_MODULE) {
    if (permissionCode.startsWith(prefix)) return module;
  }
  return null;
}
