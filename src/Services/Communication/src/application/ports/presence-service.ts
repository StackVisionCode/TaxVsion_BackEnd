import type { BusyReason } from '../../domain/presence/presence-status.js';

/**
 * Presence service — Redis con lease TTL (30s) y heartbeat. Al desconectar,
 * el socket libera el lease inmediatamente; si no llega el `unregister` (crash
 * del cliente), el lease expira solo. Cierra CRIT-4 del legacy (3 fuentes de
 * verdad + `sleep(2000)`).
 *
 * Publica cambios via Redis Pub/Sub para que otros pods informen a las UIs
 * suscritas.
 *
 * Fase A1 — `markBusy`/`clearBusy` agregan un segundo eje derivado (Busy),
 * calculado a partir de "fuentes" activas (una Call o un Meeting concretos,
 * identificados por `sourceId`), con el mismo idioma de lease TTL que
 * online/offline: si el proceso que llamo `markBusy` muere sin llamar
 * `clearBusy`, la fuente expira sola. Un usuario puede tener varias fuentes
 * activas a la vez (ej. una Call y un Meeting simultaneos) — Busy es
 * "al menos una fuente activa", no un booleano seteado directamente.
 */
export interface PresenceService {
  register(input: { tenantId: string; userId: string; sessionId: string; leaseSeconds: number }): Promise<void>;
  heartbeat(input: { tenantId: string; userId: string; sessionId: string; leaseSeconds: number }): Promise<void>;
  unregister(input: { tenantId: string; userId: string; sessionId: string }): Promise<void>;
  isOnline(tenantId: string, userId: string): Promise<boolean>;
  listOnline(tenantId: string, userIds: readonly string[]): Promise<readonly string[]>;
  markBusy(input: {
    tenantId: string;
    userId: string;
    sourceId: string;
    kind: BusyReason;
    leaseSeconds: number;
  }): Promise<void>;
  clearBusy(input: { tenantId: string; userId: string; sourceId: string }): Promise<void>;
}
