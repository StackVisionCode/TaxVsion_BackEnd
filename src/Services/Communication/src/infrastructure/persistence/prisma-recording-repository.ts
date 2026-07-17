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
    await this.prisma.recordingSession.upsert({
      where: { Scope_ScopeId: { Scope: s.scope, ScopeId: s.scopeId } },
      create: {
        Id: s.id,
        TenantId: s.tenantId,
        Scope: s.scope,
        ScopeId: s.scopeId,
        State: s.state,
        RequestedByUserId: s.requestedByUserId,
        RequestedAtUtc: s.requestedAtUtc,
        StartedAtUtc: s.startedAtUtc,
        StoppedAtUtc: s.stoppedAtUtc,
        RecordingFileId: s.recordingFileId,
        DurationSeconds: s.durationSeconds,
        FailureReason: s.failureReason,
      },
      update: {
        State: s.state,
        StartedAtUtc: s.startedAtUtc,
        StoppedAtUtc: s.stoppedAtUtc,
        RecordingFileId: s.recordingFileId,
        DurationSeconds: s.durationSeconds,
        FailureReason: s.failureReason,
      },
    });
  }

  async findByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    const row = await this.prisma.recordingSession.findFirst({
      where: { TenantId: tenantId, Scope: scope, ScopeId: scopeId },
    });
    return row ? toDomainRecordingSession(row) : null;
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
