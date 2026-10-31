import type { PrismaClient } from '@prisma/client';
import type { RecordingSession } from '../../domain/recording/recording-session.js';
import type { RecordingConsentEntry } from '../../domain/recording/recording-consent-entry.js';
import type { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type {
  RecordingSessionRepository,
  RecordingConsentRepository,
} from '../../application/ports/recording-repository.js';
import { toDomainRecordingSession, toDomainRecordingConsentEntry } from './recording-mapper.js';

export class PrismaRecordingSessionRepository implements RecordingSessionRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async save(session: RecordingSession): Promise<void> {
    const s = session.toSnapshot();
    const mutable = {
      State: s.state,
      StartedAtUtc: s.startedAtUtc,
      StoppedAtUtc: s.stoppedAtUtc,
      RecordingFileId: s.recordingFileId,
      DurationSeconds: s.durationSeconds,
      FailureReason: s.failureReason,
      CaptureMode: s.captureMode,
      MaxDurationSeconds: s.maxDurationSeconds,
      LimitWarnedAtUtc: s.limitWarnedAtUtc,
      StopReason: s.stopReason,
      ComposedFileId: s.composedFileId,
      ComposedSizeBytes: s.composedSizeBytes === null ? null : BigInt(s.composedSizeBytes),
      ComposedFormat: s.composedFormat,
      TracksManifestJson: s.tracksManifest === null ? null : JSON.stringify(s.tracksManifest),
      ComposedAtUtc: s.composedAtUtc,
      ReadyAtUtc: s.readyAtUtc,
      TracksPurgedAtUtc: s.tracksPurgedAtUtc,
      PurgedAtUtc: s.purgedAtUtc,
    };
    await this.prisma.recordingSession.upsert({
      where: { Scope_ScopeId: { Scope: s.scope, ScopeId: s.scopeId } },
      create: {
        Id: s.id,
        TenantId: s.tenantId,
        Scope: s.scope,
        ScopeId: s.scopeId,
        RequestedByUserId: s.requestedByUserId,
        RequestedAtUtc: s.requestedAtUtc,
        ...mutable,
      },
      update: mutable,
    });
  }

  async findByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    const row = await this.prisma.recordingSession.findFirst({
      where: { TenantId: tenantId, Scope: scope, ScopeId: scopeId },
    });
    return row ? toDomainRecordingSession(row) : null;
  }

  async findByScopeAnyTenant(scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    const row = await this.prisma.recordingSession.findFirst({ where: { Scope: scope, ScopeId: scopeId } });
    return row ? toDomainRecordingSession(row) : null;
  }

  async listStatesByScopeIds(tenantId: string, scope: RecordingScope, scopeIds: readonly string[]): Promise<ReadonlyMap<string, string>> {
    if (scopeIds.length === 0) return new Map();
    const rows = await this.prisma.recordingSession.findMany({
      where: { TenantId: tenantId, Scope: scope, ScopeId: { in: [...scopeIds] }, PurgedAtUtc: null },
      select: { ScopeId: true, State: true },
    });
    return new Map(rows.map((r) => [r.ScopeId, r.State]));
  }

  async listStaleRequesting(olderThanUtc: Date, scope?: RecordingScope): Promise<RecordingSession[]> {
    const rows = await this.prisma.recordingSession.findMany({
      where: {
        State: 'Requesting',
        RequestedAtUtc: { lte: olderThanUtc },
        ...(scope ? { Scope: scope } : {}),
      },
    });
    return rows.map(toDomainRecordingSession);
  }

  async findByRecordingFileId(tenantId: string, fileId: string): Promise<RecordingSession | null> {
    const row = await this.prisma.recordingSession.findFirst({
      where: { TenantId: tenantId, OR: [{ ComposedFileId: fileId }, { RecordingFileId: fileId }] },
    });
    return row ? toDomainRecordingSession(row) : null;
  }

  async listRecordingWithLimit(): Promise<RecordingSession[]> {
    const rows = await this.prisma.recordingSession.findMany({
      where: { State: 'Recording', MaxDurationSeconds: { not: null } },
    });
    return rows.map(toDomainRecordingSession);
  }

  async listProcessing(): Promise<RecordingSession[]> {
    const rows = await this.prisma.recordingSession.findMany({ where: { State: 'Processing' } });
    return rows.map(toDomainRecordingSession);
  }

  async listComposingStoppedBefore(stoppedBeforeUtc: Date): Promise<RecordingSession[]> {
    const rows = await this.prisma.recordingSession.findMany({
      where: { State: 'Composing', StoppedAtUtc: { lt: stoppedBeforeUtc } },
    });
    return rows.map(toDomainRecordingSession);
  }

  async listTracksToPurge(composedBeforeUtc: Date, take: number): Promise<RecordingSession[]> {
    const rows = await this.prisma.recordingSession.findMany({
      where: {
        CaptureMode: 'Server',
        TracksPurgedAtUtc: null,
        TracksManifestJson: { not: null },
        ComposedAtUtc: { lt: composedBeforeUtc },
      },
      orderBy: { ComposedAtUtc: 'asc' },
      take,
    });
    return rows.map(toDomainRecordingSession);
  }

  async listReadyToPurge(tenantId: string, readyBeforeUtc: Date, take: number): Promise<RecordingSession[]> {
    const rows = await this.prisma.recordingSession.findMany({
      where: { TenantId: tenantId, State: 'Ready', PurgedAtUtc: null, ReadyAtUtc: { lt: readyBeforeUtc } },
      orderBy: { ReadyAtUtc: 'asc' },
      take,
    });
    return rows.map(toDomainRecordingSession);
  }
}

export class PrismaRecordingConsentRepository implements RecordingConsentRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async append(entry: RecordingConsentEntry): Promise<void> {
    const s = entry.toSnapshot();
    await this.prisma.recordingConsentEvent.create({
      data: {
        Id: s.id,
        TenantId: s.tenantId,
        Scope: s.scope,
        ScopeId: s.scopeId,
        UserId: s.userId,
        Response: s.response,
        RespondedAtUtc: s.respondedAtUtc,
        RecordedAtUtc: s.recordedAtUtc,
      },
    });
  }

  async listByScope(
    tenantId: string,
    scope: RecordingScope,
    scopeId: string,
  ): Promise<RecordingConsentEntry[]> {
    const rows = await this.prisma.recordingConsentEvent.findMany({
      where: { TenantId: tenantId, Scope: scope, ScopeId: scopeId },
      orderBy: { RecordedAtUtc: 'asc' },
    });
    return rows.map(toDomainRecordingConsentEntry);
  }
}
