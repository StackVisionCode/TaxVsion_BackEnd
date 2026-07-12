/**
 * Presence service — Redis con lease TTL (30s) y heartbeat. Al desconectar,
 * el socket libera el lease inmediatamente; si no llega el `unregister` (crash
 * del cliente), el lease expira solo. Cierra CRIT-4 del legacy (3 fuentes de
 * verdad + `sleep(2000)`).
 *
 * Publica cambios via Redis Pub/Sub para que otros pods informen a las UIs
 * suscritas.
 */
export interface PresenceService {
  register(input: { tenantId: string; userId: string; sessionId: string; leaseSeconds: number }): Promise<void>;
  heartbeat(input: { tenantId: string; userId: string; sessionId: string; leaseSeconds: number }): Promise<void>;
  unregister(input: { tenantId: string; userId: string; sessionId: string }): Promise<void>;
  isOnline(tenantId: string, userId: string): Promise<boolean>;
  listOnline(tenantId: string, userIds: readonly string[]): Promise<readonly string[]>;
}
