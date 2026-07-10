import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import {
  MeetingEventTypes,
  type MeetingEndedEvent,
  type MeetingStartedEvent,
} from '../../contracts/events/meeting-events.js';

export interface StartMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
}

export async function startMeeting(
  cmd: StartMeetingCommand,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ startedAtUtc: string }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const now = new Date();
  const result = meeting.start({ hostUserId: cmd.hostUserId, now });
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
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ endedAtUtc: string; durationSeconds: number }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const now = new Date();
  const result = meeting.end({ byUserId: cmd.byUserId, now });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);
  const snapshot = meeting.toSnapshot();

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
    recordingFileId: snapshot.recordingFileId,
  };
  await deps.publisher.enqueue(event);
  return Result.ok({ endedAtUtc: now.toISOString(), durationSeconds: snapshot.durationSeconds ?? 0 });
}
