import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import {
  MeetingEventTypes,
  type MeetingParticipantLeftEvent,
} from '../../contracts/events/meeting-events.js';

export interface LeaveMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly userId: string;
}

export async function leaveMeeting(
  cmd: LeaveMeetingCommand,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ leftAtUtc: string }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const now = new Date();
  const result = meeting.leave({ userId: cmd.userId, now });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const snapshot = meeting.toSnapshot();
  const participant = snapshot.participants.find((p) => p.userId === cmd.userId);

  if (participant?.joinedAtUtc && participant?.leftAtUtc) {
    const durationSeconds = Math.max(
      0,
      Math.floor((participant.leftAtUtc.getTime() - participant.joinedAtUtc.getTime()) / 1000),
    );
    const event: MeetingParticipantLeftEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.ParticipantLeft,
      tenantId: cmd.tenantId,
      correlationId: cmd.correlationId,
      occurredOnUtc: participant.leftAtUtc.toISOString(),
      meetingId: cmd.meetingId,
      participantUserId: cmd.userId,
      joinedAtUtc: participant.joinedAtUtc.toISOString(),
      leftAtUtc: participant.leftAtUtc.toISOString(),
      durationSeconds,
    };
    await deps.publisher.enqueue(event);
  }

  return Result.ok({ leftAtUtc: now.toISOString() });
}
