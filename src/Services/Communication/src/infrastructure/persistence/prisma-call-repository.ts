import type { PrismaClient } from '@prisma/client';
import type { Call, CallSnapshot } from '../../domain/calls/call.js';
import type { CallRepository } from '../../application/ports/call-repository.js';
import { toDomainCall, toDomainCallParticipant } from './call-mapper.js';

export class PrismaCallRepository implements CallRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async save(call: Call): Promise<void> {
    const snapshot = call.toSnapshot();
    await this.prisma.$transaction(async (tx) => {
      await tx.call.upsert({
        where: { Id: snapshot.id },
        create: {
          Id: snapshot.id,
          TenantId: snapshot.tenantId,
          Kind: snapshot.kind,
          Status: snapshot.status,
          CallerUserId: snapshot.callerUserId,
          CalleeUserId: snapshot.calleeUserId,
          ConversationId: snapshot.conversationId,
          RingingAtUtc: snapshot.ringingAtUtc,
          AcceptedAtUtc: snapshot.acceptedAtUtc,
          StartedAtUtc: snapshot.startedAtUtc,
          EndedAtUtc: snapshot.endedAtUtc,
          EndReason: snapshot.endReason,
          DurationSeconds: snapshot.durationSeconds,
          RecordingRequested: snapshot.recordingRequested,
          RecordingFileId: snapshot.recordingFileId,
          TranscriptFileId: snapshot.transcriptFileId,
          CreatedAtUtc: snapshot.createdAtUtc,
          UpdatedAtUtc: snapshot.updatedAtUtc,
        },
        update: {
          Status: snapshot.status,
          AcceptedAtUtc: snapshot.acceptedAtUtc,
          StartedAtUtc: snapshot.startedAtUtc,
          EndedAtUtc: snapshot.endedAtUtc,
          EndReason: snapshot.endReason,
          DurationSeconds: snapshot.durationSeconds,
          RecordingFileId: snapshot.recordingFileId,
          TranscriptFileId: snapshot.transcriptFileId,
          UpdatedAtUtc: snapshot.updatedAtUtc,
        },
      });

      for (const p of snapshot.participants) {
        await tx.callParticipant.upsert({
          where: { CallId_UserId: { CallId: snapshot.id, UserId: p.userId } },
          create: {
            Id: p.id,
            CallId: snapshot.id,
            TenantId: p.tenantId,
            UserId: p.userId,
            DisplayName: p.displayName,
            Role: p.role,
            JoinOrder: p.joinOrder,
            JoinedAtUtc: p.joinedAtUtc,
            LeftAtUtc: p.leftAtUtc,
            AudioEnabled: p.audioEnabled,
            VideoEnabled: p.videoEnabled,
            ScreenSharing: p.screenSharing,
            ScreenShareStartedAtUtc: p.screenShareStartedAtUtc,
            ConnectionQuality: p.connectionQuality,
          },
          update: {
            DisplayName: p.displayName,
            LeftAtUtc: p.leftAtUtc,
            AudioEnabled: p.audioEnabled,
            VideoEnabled: p.videoEnabled,
            ScreenSharing: p.screenSharing,
            ScreenShareStartedAtUtc: p.screenShareStartedAtUtc,
            ConnectionQuality: p.connectionQuality,
          },
        });
      }
    });
  }

  async findById(tenantId: string, callId: string): Promise<Call | null> {
    const row = await this.prisma.call.findFirst({ where: { Id: callId, TenantId: tenantId } });
    if (!row) return null;
    const participants = await this.prisma.callParticipant.findMany({
      where: { CallId: callId, TenantId: tenantId },
    });
    return toDomainCall(row, participants);
  }

  async findRingingOlderThan(cutoffUtc: Date): Promise<CallSnapshot[]> {
    const rows = await this.prisma.call.findMany({
      where: { Status: 'Ringing', RingingAtUtc: { lt: cutoffUtc } },
      include: { Participants: true },
    });
    return rows.map((row) => ({
      id: row.Id,
      tenantId: row.TenantId,
      kind: row.Kind === 'Video' ? 'Video' : 'Audio',
      status: 'Ringing',
      callerUserId: row.CallerUserId,
      calleeUserId: row.CalleeUserId,
      conversationId: row.ConversationId,
      ringingAtUtc: row.RingingAtUtc,
      acceptedAtUtc: row.AcceptedAtUtc,
      startedAtUtc: row.StartedAtUtc,
      endedAtUtc: row.EndedAtUtc,
      endReason: null,
      durationSeconds: row.DurationSeconds,
      recordingRequested: row.RecordingRequested,
      recordingFileId: row.RecordingFileId,
      transcriptFileId: row.TranscriptFileId,
      createdAtUtc: row.CreatedAtUtc,
      updatedAtUtc: row.UpdatedAtUtc,
      participants: row.Participants.map(toDomainCallParticipant),
    }));
  }

  async listRecentForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
  }): Promise<CallSnapshot[]> {
    const rows = await this.prisma.call.findMany({
      where: {
        TenantId: input.tenantId,
        OR: [{ CallerUserId: input.userId }, { CalleeUserId: input.userId }],
      },
      include: { Participants: true },
      orderBy: { RingingAtUtc: 'desc' },
      take: input.take,
      skip: input.skip,
    });
    return rows.map((row) => toDomainCall(row, row.Participants).toSnapshot());
  }

  async countRecentForUser(tenantId: string, userId: string): Promise<number> {
    return this.prisma.call.count({
      where: {
        TenantId: tenantId,
        OR: [{ CallerUserId: userId }, { CalleeUserId: userId }],
      },
    });
  }
}
