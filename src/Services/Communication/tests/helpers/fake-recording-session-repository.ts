import { RecordingSequenceTakenError, type RecordingSessionRepository } from '../../src/application/ports/recording-repository.js';
import type { RecordingSession, RecordingSessionSnapshot } from '../../src/domain/recording/recording-session.js';
import { RecordingCaptureMode, type RecordingScope } from '../../src/domain/recording/recording-session-state.js';

/** Campos F7 de una sesion rehidratada en tests (captura cliente, sin limite ni composicion). */
export const F7_SNAPSHOT_DEFAULTS: Pick<
  RecordingSessionSnapshot,
  | 'captureMode'
  | 'maxDurationSeconds'
  | 'limitWarnedAtUtc'
  | 'stopReason'
  | 'composedFileId'
  | 'composedSizeBytes'
  | 'composedFormat'
  | 'tracksManifest'
  | 'composedAtUtc'
  | 'readyAtUtc'
  | 'tracksPurgedAtUtc'
  | 'purgedAtUtc'
  | 'sequence'
> = {
  captureMode: RecordingCaptureMode.Client,
  maxDurationSeconds: null,
  limitWarnedAtUtc: null,
  stopReason: null,
  composedFileId: null,
  composedSizeBytes: null,
  composedFormat: null,
  tracksManifest: null,
  composedAtUtc: null,
  readyAtUtc: null,
  tracksPurgedAtUtc: null,
  purgedAtUtc: null,
  sequence: 1,
};

/** Repositorio en memoria con la misma semantica que el de Prisma (upsert por Id; varias por scope, D-71). */
export class InMemoryRecordingSessionRepository implements RecordingSessionRepository {
  protected readonly store = new Map<string, RecordingSession>();

  private all(): RecordingSession[] {
    return [...this.store.values()];
  }

  private ofScope(scope: string, scopeId: string): RecordingSession[] {
    return this.all()
      .filter((session) => session.scope === scope && session.scopeId === scopeId)
      .sort((a, b) => a.sequence - b.sequence);
  }

  async save(session: RecordingSession): Promise<void> {
    const s = session.toSnapshot();
    const clash = this.ofScope(s.scope, s.scopeId).find((other) => other.sequence === s.sequence && other.id !== s.id);
    if (clash) throw new RecordingSequenceTakenError();
    this.store.set(s.id, session);
  }

  async findById(tenantId: string, sessionId: string): Promise<RecordingSession | null> {
    const session = this.store.get(sessionId);
    return session && session.toSnapshot().tenantId === tenantId ? session : null;
  }

  async findLatestByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    const session = this.ofScope(scope, scopeId).at(-1);
    return session && session.toSnapshot().tenantId === tenantId ? session : null;
  }

  async listByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession[]> {
    return this.ofScope(scope, scopeId).filter((session) => session.toSnapshot().tenantId === tenantId);
  }

  async findLatestByScopeAnyTenant(scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    return this.ofScope(scope, scopeId).at(-1) ?? null;
  }

  async listStatesByScopeIds(tenantId: string, scope: RecordingScope, scopeIds: readonly string[]): Promise<ReadonlyMap<string, string>> {
    const states = new Map<string, string>();
    for (const id of scopeIds) {
      const s = (await this.findLatestByScope(tenantId, scope, id))?.toSnapshot();
      if (s && !s.purgedAtUtc) states.set(id, s.state);
    }
    return states;
  }

  async listStaleRequesting(olderThanUtc: Date, scope?: RecordingScope): Promise<RecordingSession[]> {
    return this.all().filter((session) => {
      const s = session.toSnapshot();
      return s.state === 'Requesting' && s.requestedAtUtc.getTime() <= olderThanUtc.getTime() && (!scope || s.scope === scope);
    });
  }

  async listCaptureActiveClientByRequester(tenantId: string, requestedByUserId: string): Promise<RecordingSession[]> {
    return this.all().filter((session) => {
      const s = session.toSnapshot();
      return (
        s.tenantId === tenantId &&
        s.requestedByUserId === requestedByUserId &&
        s.captureMode === 'Client' &&
        (s.state === 'Requesting' || s.state === 'Recording' || s.state === 'Stopping')
      );
    });
  }

  async findByRecordingFileId(tenantId: string, fileId: string): Promise<RecordingSession | null> {
    return this.all().find((session) => {
      const s = session.toSnapshot();
      return s.tenantId === tenantId && s.recordingFileId === fileId;
    }) ?? null;
  }

  async listRecordingWithLimit(): Promise<RecordingSession[]> {
    return this.all().filter((session) => {
      const s = session.toSnapshot();
      return s.state === 'Recording' && s.maxDurationSeconds !== null;
    });
  }

  async listProcessing(): Promise<RecordingSession[]> {
    return this.all().filter((session) => session.toSnapshot().state === 'Processing');
  }

  async listComposingStoppedBefore(stoppedBeforeUtc: Date): Promise<RecordingSession[]> {
    return this.all().filter((session) => {
      const s = session.toSnapshot();
      return s.state === 'Composing' && !!s.stoppedAtUtc && s.stoppedAtUtc.getTime() < stoppedBeforeUtc.getTime();
    });
  }

  async listTracksToPurge(composedBeforeUtc: Date, take: number): Promise<RecordingSession[]> {
    return this.all()
      .filter((session) => {
        const s = session.toSnapshot();
        return !!s.composedAtUtc && s.composedAtUtc.getTime() < composedBeforeUtc.getTime() && !s.tracksPurgedAtUtc && !!s.tracksManifest;
      })
      .slice(0, take);
  }

  async listReadyToPurge(tenantId: string, readyBeforeUtc: Date, take: number): Promise<RecordingSession[]> {
    return this.all()
      .filter((session) => {
        const s = session.toSnapshot();
        return s.tenantId === tenantId && s.state === 'Ready' && !!s.readyAtUtc && s.readyAtUtc.getTime() < readyBeforeUtc.getTime() && !s.purgedAtUtc;
      })
      .slice(0, take);
  }
}
