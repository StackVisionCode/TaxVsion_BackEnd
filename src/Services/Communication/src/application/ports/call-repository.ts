import type { Call, CallSnapshot } from '../../domain/calls/call.js';

/**
 * Repositorio del aggregate Call. Reglas identicas al ConversationRepository:
 *   - Filtro TenantId obligatorio en TODA query.
 *   - `save` es transaccional root + participants.
 *   - `findRinging` es usado por el MissedCallScheduler.
 */
export interface CallRepository {
  save(call: Call): Promise<void>;
  findById(tenantId: string, callId: string): Promise<Call | null>;
  /**
   * Cross-tenant lookup por diseno: escaneado por MissedCallScheduler para
   * marcar Missed las llamadas Ringing con timeout. Cada call publicara su
   * evento con su TenantId correcto (viene del snapshot).
   */
  findRingingOlderThan(cutoffUtc: Date): Promise<CallSnapshot[]>;
  listRecentForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
  }): Promise<CallSnapshot[]>;
  countRecentForUser(tenantId: string, userId: string): Promise<number>;
}
