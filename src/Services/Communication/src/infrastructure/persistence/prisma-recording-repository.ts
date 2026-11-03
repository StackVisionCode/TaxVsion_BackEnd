import { Prisma, type PrismaClient } from '@prisma/client';
import type { RecordingSession } from '../../domain/recording/recording-session.js';
import type { RecordingConsentEntry } from '../../domain/recording/recording-consent-entry.js';
import type { RecordingScope } from '../../domain/recording/recording-session-state.js';
import {
  RecordingSequenceTakenError,
  type RecordingSessionRepository,
  type RecordingConsentRepository,
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
    // Dos pedidos a la vez con el mismo numero chocan en el @@unique (Scope, ScopeId, Sequence).
    await this.prisma.recordingSession
      .upsert({
      where: { Id: s.id },
      create: {
        Id: s.id,
        TenantId: s.tenantId,
        Scope: s.scope,
        ScopeId: s.scopeId,
        Sequence: s.sequence,
        RequestedByUserId: s.requestedByUserId,
        RequestedAtUtc: s.requestedAtUtc,
        ...mutable,
      },
      update: mutable,
      })
      .catch((err: unknown) => {
        if (err instanceof Prisma.PrismaClientKnownRequestError && err.code === 'P2002') throw new RecordingSequenceTakenError();
        throw err;
      });
  }

  async findById(tenantId: string, sessionId: string): Promise<RecordingSession | null> {
    const row = await this.prisma.recordingSession.findFirst({ where: { TenantId: tenantId, Id: sessionId } });
    return row ? toDomainRecordingSession(row) : null;
  }

  async findLatestByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    const row = await this.prisma.recordingSession.findFirst({
      where: { TenantId: tenantId, Scope: scope, ScopeId: scopeId },
      orderBy: { Sequence: 'desc' },
    });
    return row ? toDomainRecordingSession(row) : null;
  }

  async listByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession[]> {
    const rows = await this.prisma.recordingSession.findMany({
      where: { TenantId: tenantId, Scope: scope, ScopeId: scopeId },
      orderBy: { Sequence: 'asc' },
    });
    return rows.map(toDomainRecordingSession);
  }

  async findLatestByScopeAnyTenant(scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    const row = await this.prisma.recordingSession.findFirst({
      where: { Scope: scope, ScopeId: scopeId },
      orderBy: { Sequence: 'desc' },
    });
    return row ? toDomainRecordingSession(row) : null;
  }

  async listStatesByScopeIds(tenantId: string, scope: RecordingScope, scopeIds: readonly string[]): Promise<ReadonlyMap<string, string>> {
    if (scopeIds.length === 0) return new Map();
    const rows = await this.prisma.recordingSession.findMany({
      where: { TenantId: tenantId, Scope: scope, ScopeId: { in: [...scopeIds] }, PurgedAtUtc: null },
      select: { ScopeId: true, State: true },
      orderBy: { Sequence: 'asc' },
    });
    // Orden ascendente: la ultima grabacion de cada scope pisa a las anteriores.
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
