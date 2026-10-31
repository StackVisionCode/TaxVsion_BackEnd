import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { Meeting } from '../../domain/meetings/meeting.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { MeetingEventTypes, type MeetingHostTransferredEvent } from '../../contracts/events/meeting-events.js';
import {
  MeetingSocketEvents,
  type MeetingHostChangedDto,
  type MeetingStateDto,
} from '../../contracts/socket/meeting-socket-events.js';
import { participantSnapshotToDto } from './meeting-mappers.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';

export interface SucceedHostDeps extends MeetingEventLogDeps {
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter?: RealtimeEmitter | undefined;
}

export type SucceedHostResult =
  | { readonly outcome: 'succeeded'; readonly newHostUserId: string }
  | { readonly outcome: 'ended'; readonly meeting: Meeting };

/**
 * Sucesión automática del host (D-08, T-06) cuando venció su gracia sin reconectar. Si el dominio
 * encuentra sucesor: dos `participant.changed` (host saliente → cohost, sucesor → host) y después
 * `meeting.host.changed` + `state.changed` con el estado real, en ese orden (INV-07 §13.1). Si no
 * queda staff, el dominio ya terminó la reunión: el caller hace el cierre ordenado.
 */
export async function succeedHost(
  input: { tenantId: string; meetingId: string; correlationId: string },
  deps: SucceedHostDeps,
): Promise<Result<SucceedHostResult>> {
  const meeting = await deps.meetings.findById(input.tenantId, input.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const previousHostUserId = meeting.hostUserId;
  const now = new Date();
  const result = meeting.succeedHost({ now });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  if (result.value.outcome === 'ended') {
    return Result.ok({ outcome: 'ended', meeting });
  }

  const newHostUserId = result.value.newHostUserId;
  const snapshot = meeting.toSnapshot();
  const event: MeetingHostTransferredEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.HostTransferred,
    tenantId: input.tenantId,
    correlationId: input.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: input.meetingId,
    previousHostUserId,
    newHostUserId,
    transferredAtUtc: now.toISOString(),
    reason: 'Succession',
  };
  await deps.publisher.enqueue(event);
  await recordMeetingEvent(deps, {
    tenantId: input.tenantId,
    meetingId: input.meetingId,
    type: MeetingEventType.HostSucceeded,
    actorUserId: null,
    targetUserId: newHostUserId,
    payload: { previousHostUserId },
    correlationId: input.correlationId,
    at: now,
  });

  if (deps.emitter) {
    const envelope = <T>(payload: T) => ({
      eventId: randomUUID(),
      correlationId: input.correlationId,
      emittedAtUtc: now.toISOString(),
      payload,
    });
    for (const userId of [previousHostUserId, newHostUserId]) {
      const participant = snapshot.participants.find((p) => p.userId === userId);
      if (!participant) continue;
      deps.emitter.emitToMeeting({
        tenantId: input.tenantId,
        meetingId: input.meetingId,
        event: MeetingSocketEvents.ParticipantChanged,
        envelope: envelope({ meetingId: input.meetingId, participant: participantSnapshotToDto(participant), sequence: 0 }),
      });
    }
    const hostChanged: MeetingHostChangedDto = { meetingId: input.meetingId, previousHostUserId, newHostUserId, reason: 'Succession' };
    deps.emitter.emitToMeeting({ tenantId: input.tenantId, meetingId: input.meetingId, event: MeetingSocketEvents.HostChanged, envelope: envelope(hostChanged) });
    const state: MeetingStateDto = {
      meetingId: input.meetingId,
      status: snapshot.status,
      isLocked: snapshot.isLocked,
      hostUserId: snapshot.hostUserId,
      sequence: 0,
    };
    deps.emitter.emitToMeeting({ tenantId: input.tenantId, meetingId: input.meetingId, event: MeetingSocketEvents.StateChanged, envelope: envelope(state) });
  }

  return Result.ok({ outcome: 'succeeded', newHostUserId });
}
