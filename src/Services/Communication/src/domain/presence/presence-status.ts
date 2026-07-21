/**
 * Estado de presencia derivado (Fase A1). Eje separado de "online/offline":
 * un usuario Busy sigue conectado (tiene sesiones activas), solo que tiene
 * una Call o un Meeting activos ahora mismo. Ver RFC 6121 <show/> — mismo
 * principio de separar disponibilidad de estado detallado.
 */
export const PresenceStatus = {
  Online: 'Online',
  Busy: 'Busy',
  Offline: 'Offline',
} as const;
export type PresenceStatus = (typeof PresenceStatus)[keyof typeof PresenceStatus];

export function isPresenceStatus(value: string): value is PresenceStatus {
  return value === 'Online' || value === 'Busy' || value === 'Offline';
}

export const BusyReason = {
  Call: 'Call',
  Meeting: 'Meeting',
} as const;
export type BusyReason = (typeof BusyReason)[keyof typeof BusyReason];

export function isBusyReason(value: string): value is BusyReason {
  return value === 'Call' || value === 'Meeting';
}
