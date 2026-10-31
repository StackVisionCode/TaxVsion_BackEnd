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
  /** F7 (admin PlatformOnly): la sesion de un scope sin conocer la oficina (los ids son GUID globales). */
  findByScopeAnyTenant(scope: RecordingScope, scopeId: string): Promise<RecordingSession | null>;
  /** F7 (contrato 9.6): estado de grabacion por scope para listas (una consulta por pagina). */
  listStatesByScopeIds(tenantId: string, scope: RecordingScope, scopeIds: readonly string[]): Promise<ReadonlyMap<string, string>>;
  /**
   * Cross-tenant por diseno — consumido por recording-consent-timeout-scheduler.ts, que corre una
   * unica vez por proceso, no por tenant. `scope` opcional para filtrar calls con su timeout.
   */
  listStaleRequesting(olderThanUtc: Date, scope?: RecordingScope): Promise<RecordingSession[]>;
  /** F7: la sesion cuyo archivo de grabacion (compuesto o subido por el cliente) es `fileId`. */
  findByRecordingFileId(tenantId: string, fileId: string): Promise<RecordingSession | null>;
  /** F7 (T-35, cross-tenant): sesiones grabando con limite, para avisar y cortar. */
  listRecordingWithLimit(): Promise<RecordingSession[]>;
  /** F7 (cross-tenant): sesiones esperando a CloudStorage, para el respaldo de disponibilidad. */
  listProcessing(): Promise<RecordingSession[]>;
  /** F7 (cross-tenant): composiciones que no volvieron del recorder antes de `stoppedBeforeUtc`. */
  listComposingStoppedBefore(stoppedBeforeUtc: Date): Promise<RecordingSession[]>;
  /** F7 (T-37, cross-tenant): compuestos con pistas de audio aun sin purgar compuestos antes de `composedBeforeUtc`. */
  listTracksToPurge(composedBeforeUtc: Date, take: number): Promise<RecordingSession[]>;
  /** F7 (T-37): grabaciones Ready de la oficina listas antes de `readyBeforeUtc`, aun sin purgar. */
  listReadyToPurge(tenantId: string, readyBeforeUtc: Date, take: number): Promise<RecordingSession[]>;
}

/** Append-only — no hay update()/delete(), solo insertar y listar. */
export interface RecordingConsentRepository {
  append(entry: RecordingConsentEntry): Promise<void>;
  listByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingConsentEntry[]>;
}
