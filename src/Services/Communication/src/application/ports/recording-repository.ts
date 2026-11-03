import type { RecordingSession } from '../../domain/recording/recording-session.js';
import type { RecordingConsentEntry } from '../../domain/recording/recording-consent-entry.js';
import type { RecordingScope } from '../../domain/recording/recording-session-state.js';

/** D-71: otro pedido simultaneo ya tomo ese numero de grabacion (@@unique Scope, ScopeId, Sequence). */
export class RecordingSequenceTakenError extends Error {
  constructor() {
    super('Another recording was requested at the same time.');
    this.name = 'RecordingSequenceTakenError';
  }
}

/**
 * D-71: varias grabaciones por (Scope, ScopeId), numeradas por `sequence`; save() es upsert por Id y
 * lanza `RecordingSequenceTakenError` si el numero ya esta tomado.
 */
export interface RecordingSessionRepository {
  save(session: RecordingSession): Promise<void>;
  findById(tenantId: string, sessionId: string): Promise<RecordingSession | null>;
  /** La grabacion mas reciente del meeting/call (la que puede estar capturando). */
  findLatestByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession | null>;
  /** Todas las grabaciones del meeting/call, de la primera a la ultima. */
  listByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession[]>;
  /** F7 (admin PlatformOnly): la grabacion mas reciente de un scope sin conocer la oficina (los ids son GUID globales). */
  findLatestByScopeAnyTenant(scope: RecordingScope, scopeId: string): Promise<RecordingSession | null>;
  /** F7 (contrato 9.6): estado de la ultima grabacion por scope para listas (una consulta por pagina). */
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
