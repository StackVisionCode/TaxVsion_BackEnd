import type { RecordingSession } from '../../domain/recording/recording-session.js';
import type { RecordingConsentEntry } from '../../domain/recording/recording-consent-entry.js';
import type { RecordingScope } from '../../domain/recording/recording-session-state.js';

/**
 * Un unico RecordingSession existe por (Scope, ScopeId) — save() es upsert
 * por diseno (Prisma @@unique([Scope, ScopeId])), nunca crea una segunda fila.
 */
export interface RecordingSessionRepository {
  save(session: RecordingSession): Promise<void>;
  findByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession | null>;
  /**
   * Cross-tenant por diseno — consumido por recording-consent-timeout-scheduler.ts
   * (Fase Backend 3), que corre una unica vez por proceso, no por tenant.
   * `scope` es opcional para que Fase Backend 4 (calls) pueda reusar el mismo
   * metodo filtrando por 'Call' con un timeout mas corto.
   */
  listStaleRequesting(olderThanUtc: Date, scope?: RecordingScope): Promise<RecordingSession[]>;
}

/** Append-only — no hay update()/delete(), solo insertar y listar. */
export interface RecordingConsentRepository {
  append(entry: RecordingConsentEntry): Promise<void>;
  listByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingConsentEntry[]>;
}
