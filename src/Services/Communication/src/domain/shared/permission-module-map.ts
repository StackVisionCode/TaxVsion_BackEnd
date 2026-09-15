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
  ['communication.', 'comms'],
  ['comms.', 'comms'],
  ['campaigns.', 'campaigns'],
  ['reports.', 'reports'],
];

/** El modulo comercial del permiso, o `null` si no esta gateado por modulo (siempre efectivo). */
export function moduleFor(permissionCode: string): string | null {
  if (!permissionCode) return null;
  for (const [prefix, module] of PREFIX_TO_MODULE) {
    if (permissionCode.startsWith(prefix)) return module;
  }
  return null;
}
