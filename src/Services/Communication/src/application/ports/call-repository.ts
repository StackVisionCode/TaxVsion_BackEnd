import type { Call, CallSnapshot } from '../../domain/calls/call.js';

/** Con quien fue la llamada: un cliente (algun participante con customerId) o solo el equipo. */
export type CallAudience = 'client' | 'team';

/** Filtros del historial propio (pagina Calls). */
export interface CallHistoryFilter {
  readonly audience?: CallAudience;
  /** Solo las que ya tienen la grabacion lista. */
  readonly recordedOnly?: boolean;
  /** Parte del nombre del otro participante. */
  readonly search?: string;
}

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
    filter?: CallHistoryFilter;
  }): Promise<CallSnapshot[]>;
  countRecentForUser(tenantId: string, userId: string, filter?: CallHistoryFilter): Promise<number>;
  /** D-58: por la columna CallParticipant.CustomerId. */
  listRecentForCustomer(input: { tenantId: string; customerId: string; take: number; skip: number }): Promise<CallSnapshot[]>;
  /** F4: llamadas en curso cuyo tope vence antes del corte (scheduler, cross-tenant). */
  listEndingBefore(cutoffUtc: Date): Promise<Call[]>;
  /** Aceptadas o en curso aceptadas antes del corte (barrido de abandonadas, cross-tenant). */
  listOpenAcceptedBefore(cutoffUtc: Date, take: number): Promise<Call[]>;
  /** F4: abiertas (Ringing/Accepted/Active) de la oficina, para `maxConcurrentCalls`. */
  countOpenForTenant(tenantId: string): Promise<number>;
}
