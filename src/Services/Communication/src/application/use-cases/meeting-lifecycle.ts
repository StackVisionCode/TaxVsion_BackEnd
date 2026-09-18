import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  MeetingEventTypes,
  type MeetingEndedEvent,
  type MeetingStartedEvent,
} from '../../contracts/events/meeting-events.js';
import { MeetingSocketEvents, type MeetingListChangedDto } from '../../contracts/socket/meeting-socket-events.js';

export interface StartMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly audioDefault?: boolean;
  readonly videoDefault?: boolean;
}

export async function startMeeting(
  cmd: StartMeetingCommand,
  deps: {
    meetings: MeetingRepository;
    publisher: IntegrationEventPublisher;
    /** Opcional (best-effort): empuja `meeting.started` a invitados/participantes. */
    emitter?: RealtimeEmitter;
  },
): Promise<Result<{ startedAtUtc: string }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const now = new Date();
  const result = meeting.start({
    hostUserId: cmd.hostUserId,
    ...(cmd.audioDefault !== undefined ? { audioDefault: cmd.audioDefault } : {}),
    ...(cmd.videoDefault !== undefined ? { videoDefault: cmd.videoDefault } : {}),
    now,
  });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const event: MeetingStartedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.Started,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: cmd.meetingId,
    hostUserId: cmd.hostUserId,
    startedAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);

  // Realtime: los invitados/participantes ven el meeting pasar a "live" (join disponible) sin
  // recargar. Se excluye al host (que ya parchea su fila localmente). Best-effort.
  if (deps.emitter) {
    const snapshot = meeting.toSnapshot();
    const invitations = await deps.meetings.listInvitationsByMeeting(cmd.tenantId, cmd.meetingId);
    const inviteeUserIds = invitations
      .map((inv) => inv.toSnapshot())
      .filter((s) => s.revokedAtUtc === null && s.inviteeUserId !== null)
      .map((s) => s.inviteeUserId!);
    const targets = new Set<string>([...inviteeUserIds, ...snapshot.participants.map((p) => p.userId)]);
    targets.delete(cmd.hostUserId);
    const dto: MeetingListChangedDto = { meetingId: cmd.meetingId };
    for (const userId of targets) {
      deps.emitter.emitToUser({
        tenantId: cmd.tenantId,
        userId,
        event: MeetingSocketEvents.Started,
        envelope: {
          eventId: randomUUID(),
          correlationId: cmd.correlationId,
          emittedAtUtc: now.toISOString(),
          payload: dto,
        },
      });
    }
  }

  return Result.ok({ startedAtUtc: now.toISOString() });
}

export interface EndMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly byUserId: string;
}

export async function endMeeting(
  cmd: EndMeetingCommand,
  deps: {
    meetings: MeetingRepository;
    publisher: IntegrationEventPublisher;
    /** Opcional (best-effort): avisa `meeting.ended` a las listas de participantes/invitados. */
    emitter?: RealtimeEmitter;
  },
): Promise<Result<{ endedAtUtc: string; durationSeconds: number }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const now = new Date();
  const result = meeting.end({ byUserId: cmd.byUserId, now });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);
  const snapshot = meeting.toSnapshot();

  // Realtime: participantes/invitados ven la fila pasar de "upcoming" a "past" sin recargar.
  if (deps.emitter) {
    const invitations = await deps.meetings.listInvitationsByMeeting(cmd.tenantId, cmd.meetingId);
    const inviteeUserIds = invitations
      .map((inv) => inv.toSnapshot())
      .filter((s) => s.revokedAtUtc === null && s.inviteeUserId !== null)
      .map((s) => s.inviteeUserId!);
    const dto: MeetingListChangedDto = { meetingId: cmd.meetingId };
    for (const uid of new Set<string>([...snapshot.participants.map((p) => p.userId), ...inviteeUserIds])) {
      deps.emitter.emitToUser({
        tenantId: cmd.tenantId,
        userId: uid,
        event: MeetingSocketEvents.Ended,
        envelope: {
          eventId: randomUUID(),
          correlationId: cmd.correlationId,
          emittedAtUtc: now.toISOString(),
          payload: dto,
        },
      });
    }
  }

  const event: MeetingEndedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.Ended,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: cmd.meetingId,
    hostUserId: snapshot.hostUserId,
    endedAtUtc: now.toISOString(),
    durationSeconds: snapshot.durationSeconds ?? 0,
    participantCount: snapshot.participants.length,
    recordingFileId: snapshot.recordingFileId,
  };
  await deps.publisher.enqueue(event);
  return Result.ok({ endedAtUtc: now.toISOString(), durationSeconds: snapshot.durationSeconds ?? 0 });
}
