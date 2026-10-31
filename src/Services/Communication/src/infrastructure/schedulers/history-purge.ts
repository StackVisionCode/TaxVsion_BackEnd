import type { PrismaClient } from '@prisma/client';
import { CallStatus } from '../../domain/calls/call-status.js';
import { MeetingStatus } from '../../domain/meetings/meeting-enums.js';
import { RecordingSessionState } from '../../domain/recording/recording-session-state.js';

const DAY_MS = 86_400_000;

const TERMINAL_CALL_STATUSES = [
  CallStatus.Ended,
  CallStatus.Rejected,
  CallStatus.Cancelled,
  CallStatus.MissedCall,
  CallStatus.Failed,
] as const;
const TERMINAL_MEETING_STATUSES = [MeetingStatus.Ended, MeetingStatus.Cancelled, MeetingStatus.Expired] as const;
/** Sesiones que nunca dejaron archivo: se pueden borrar aunque no estén marcadas como purgadas. */
const FILELESS_SESSION_STATES = [RecordingSessionState.Idle, RecordingSessionState.Requesting, RecordingSessionState.Failed] as const;

export interface HistoryRetention {
  readonly callsRetentionDays: number | null;
  readonly meetingsRetentionDays: number | null;
  readonly invitationsRetentionDays: number | null;
}

export interface HistoryPurgeResult {
  readonly calls: number;
  readonly meetings: number;
  readonly invitations: number;
}

/**
 * F10 (ADD-08 T-73): histórico de llamadas, reuniones e invitaciones con retención de plataforma
 * (null = no se purga). Solo se borra lo terminado, más viejo que el corte y **sin archivos vivos**:
 * la grabación y el transcript los borra antes su propia retención (F7, T-37) y avisa a CloudStorage;
 * borrar aquí una fila con archivos dejaría huérfanos en el almacenamiento. Lotes acotados por tick.
 */
export async function purgeCommunicationHistory(
  prisma: PrismaClient,
  input: { now: Date; retention: HistoryRetention; batchSize: number },
): Promise<HistoryPurgeResult> {
  const calls =
    input.retention.callsRetentionDays !== null
      ? await purgeScopes(prisma, 'Call', cutoff(input.now, input.retention.callsRetentionDays), input.batchSize)
      : 0;
  const meetings =
    input.retention.meetingsRetentionDays !== null
      ? await purgeScopes(prisma, 'Meeting', cutoff(input.now, input.retention.meetingsRetentionDays), input.batchSize)
      : 0;
  const invitations =
    input.retention.invitationsRetentionDays !== null
      ? await purgeClosedInvitations(prisma, cutoff(input.now, input.retention.invitationsRetentionDays), input.batchSize)
      : 0;
  return { calls, meetings, invitations };
}

function cutoff(now: Date, days: number): Date {
  return new Date(now.getTime() - days * DAY_MS);
}

async function purgeScopes(prisma: PrismaClient, scope: 'Call' | 'Meeting', before: Date, batchSize: number): Promise<number> {
  const candidates =
    scope === 'Call'
      ? await prisma.call.findMany({
          where: {
            Status: { in: [...TERMINAL_CALL_STATUSES] },
            OR: [{ EndedAtUtc: { lt: before } }, { EndedAtUtc: null, UpdatedAtUtc: { lt: before } }],
            RecordingFileId: null,
            TranscriptFileId: null,
          },
          select: { Id: true },
          take: batchSize,
        })
      : await prisma.meeting.findMany({
          where: {
            Status: { in: [...TERMINAL_MEETING_STATUSES] },
            OR: [{ EndedAtUtc: { lt: before } }, { EndedAtUtc: null, UpdatedAtUtc: { lt: before } }],
            RecordingFileId: null,
            TranscriptFileId: null,
          },
          select: { Id: true },
          take: batchSize,
        });
  if (candidates.length === 0) return 0;
  const ids = candidates.map((c) => c.Id);

  // Fuera los que aún tienen una grabación o un transcript con archivos.
  const [liveSessions, liveTranscripts] = await Promise.all([
    prisma.recordingSession.findMany({
      where: { Scope: scope, ScopeId: { in: ids }, PurgedAtUtc: null, State: { notIn: [...FILELESS_SESSION_STATES] } },
      select: { ScopeId: true },
    }),
    prisma.transcriptArtifact.findMany({
      where: { Scope: scope, ScopeId: { in: ids }, OR: [{ TxtFileId: { not: null } }, { JsonFileId: { not: null } }] },
      select: { ScopeId: true },
    }),
  ]);
  const blocked = new Set([...liveSessions, ...liveTranscripts].map((r) => r.ScopeId));
  const purgeable = ids.filter((id) => !blocked.has(id));
  if (purgeable.length === 0) return 0;

  await prisma.$transaction([
    prisma.recordingSession.deleteMany({ where: { Scope: scope, ScopeId: { in: purgeable } } }),
    prisma.transcriptArtifact.deleteMany({ where: { Scope: scope, ScopeId: { in: purgeable } } }),
    prisma.recordingConsentEvent.deleteMany({ where: { Scope: scope, ScopeId: { in: purgeable } } }),
    prisma.meetingEventLog.deleteMany({ where: { ScopeId: { in: purgeable } } }),
    // Participantes (y en reuniones, invitaciones) caen por cascada.
    scope === 'Call'
      ? prisma.call.deleteMany({ where: { Id: { in: purgeable } } })
      : prisma.meeting.deleteMany({ where: { Id: { in: purgeable } } }),
  ]);
  return purgeable.length;
}

/** Invitaciones ya cerradas (revocadas, cerradas con la reunión o vencidas) antes del corte. */
async function purgeClosedInvitations(prisma: PrismaClient, before: Date, batchSize: number): Promise<number> {
  const rows = await prisma.meetingInvitation.findMany({
    where: {
      OR: [{ RevokedAtUtc: { lt: before } }, { ClosedAtUtc: { lt: before } }, { ExpiresAtUtc: { lt: before } }],
    },
    select: { Id: true },
    take: batchSize,
  });
  if (rows.length === 0) return 0;
  const result = await prisma.meetingInvitation.deleteMany({ where: { Id: { in: rows.map((r) => r.Id) } } });
  return result.count;
}
