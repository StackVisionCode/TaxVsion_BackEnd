import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  MeetingEventTypes,
  type MeetingRescheduledEvent,
} from '../../contracts/events/meeting-events.js';
import { MeetingSocketEvents, type MeetingRescheduledDto } from '../../contracts/socket/meeting-socket-events.js';
import { logHostAction } from './host-audit.js';

export interface RescheduleMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  /** ISO 8601 string; null = des-agendar (mantiene el meeting en Scheduled pero sin fecha). */
  readonly newScheduledForUtc: string | null;
}

export interface RescheduleMeetingDeps {
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

export async function rescheduleMeeting(
  cmd: RescheduleMeetingCommand,
  deps: RescheduleMeetingDeps,
): Promise<Result<void>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const previousScheduledForUtc = meeting.toSnapshot().scheduledForUtc;
  const newDate = cmd.newScheduledForUtc ? new Date(cmd.newScheduledForUtc) : null;
  if (newDate && Number.isNaN(newDate.getTime())) {
    return Result.fail(makeError('Meeting.BadPayload', 'newScheduledForUtc is not a valid ISO date.'));
  }

  const result = meeting.reschedule({ hostUserId: cmd.hostUserId, newScheduledForUtc: newDate });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const snapshot = meeting.toSnapshot();
  const participantUserIds = snapshot.participants.map((p) => p.userId);
  const invitations = await deps.meetings.listInvitationsByMeeting(cmd.tenantId, cmd.meetingId);
  const invitedEmails = invitations
    .map((inv) => inv.toSnapshot())
    .filter((s) => s.usedAtUtc === null && s.revokedAtUtc === null && s.inviteeEmail !== null)
    .map((s) => s.inviteeEmail!);

  const now = new Date();
  const newIso = newDate ? newDate.toISOString() : null;
  const prevIso = previousScheduledForUtc ? previousScheduledForUtc.toISOString() : null;

  const event: MeetingRescheduledEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.Rescheduled,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: cmd.meetingId,
    rescheduledByUserId: cmd.hostUserId,
    previousScheduledForUtc: prevIso,
    newScheduledForUtc: newIso,
    rescheduledAtUtc: now.toISOString(),
    participantUserIds,
    invitedEmails,
  };
  await deps.publisher.enqueue(event);

  const dto: MeetingRescheduledDto = {
    meetingId: cmd.meetingId,
    rescheduledByUserId: cmd.hostUserId,
    previousScheduledForUtc: prevIso,
    newScheduledForUtc: newIso,
    rescheduledAtUtc: now.toISOString(),
  };
  deps.emitter.emitToMeeting({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    event: MeetingSocketEvents.Rescheduled,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: now.toISOString(),
      payload: dto,
    },
  });

  logHostAction({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    action: 'meeting.reschedule',
    actorUserId: cmd.hostUserId,
    correlationId: cmd.correlationId,
    metadata: { previousScheduledForUtc: prevIso, newScheduledForUtc: newIso },
  });

  return Result.okVoid();
}
