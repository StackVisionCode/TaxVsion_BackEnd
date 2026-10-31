import type { RecordingSessionRepository } from '../../src/application/ports/recording-repository.js';
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
};

/** Repositorio en memoria con la misma semantica que el de Prisma (upsert por scope). */
export class InMemoryRecordingSessionRepository implements RecordingSessionRepository {
  protected readonly store = new Map<string, RecordingSession>();

  private key(scope: string, scopeId: string): string {
    return `${scope}:${scopeId}`;
  }

  private all(): RecordingSession[] {
    return [...this.store.values()];
  }

  async save(session: RecordingSession): Promise<void> {
    const s = session.toSnapshot();
    this.store.set(this.key(s.scope, s.scopeId), session);
  }

  async findByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    const session = this.store.get(this.key(scope, scopeId));
    return session && session.toSnapshot().tenantId === tenantId ? session : null;
  }

  async findByScopeAnyTenant(scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    return this.store.get(this.key(scope, scopeId)) ?? null;
  }

  async listStatesByScopeIds(tenantId: string, scope: RecordingScope, scopeIds: readonly string[]): Promise<ReadonlyMap<string, string>> {
    const states = new Map<string, string>();
    for (const id of scopeIds) {
      const s = (await this.findByScope(tenantId, scope, id))?.toSnapshot();
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
