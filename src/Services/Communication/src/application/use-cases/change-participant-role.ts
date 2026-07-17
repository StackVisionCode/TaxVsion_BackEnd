import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  MeetingEventTypes,
  type MeetingParticipantRoleChangedEvent,
} from '../../contracts/events/meeting-events.js';
import { MeetingSocketEvents, type MeetingParticipantDto } from '../../contracts/socket/meeting-socket-events.js';
import { participantSnapshotToDto } from './meeting-mappers.js';
import { logHostAction } from './host-audit.js';

export interface ChangeParticipantRoleCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly targetUserId: string;
}

export interface ChangeParticipantRoleDeps {
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

async function changeRole(
  cmd: ChangeParticipantRoleCommand,
  direction: 'promote' | 'demote',
  deps: ChangeParticipantRoleDeps,
): Promise<Result<{ participant: MeetingParticipantDto }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const previous = meeting.toSnapshot().participants.find((p) => p.userId === cmd.targetUserId);
  if (!previous) return Result.fail(makeError('Meeting.NotFound', 'Target participant not found.'));

  const result =
    direction === 'promote'
      ? meeting.promoteToCohost({ hostUserId: cmd.hostUserId, targetUserId: cmd.targetUserId })
      : meeting.demoteToAttendee({ hostUserId: cmd.hostUserId, targetUserId: cmd.targetUserId });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const now = new Date();
  const after = meeting.toSnapshot().participants.find((p) => p.userId === cmd.targetUserId);
  if (!after) return Result.fail(makeError('Meeting.NotFound', 'Target vanished after role change.'));

  const event: MeetingParticipantRoleChangedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.ParticipantRoleChanged,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: cmd.meetingId,
    participantUserId: cmd.targetUserId,
    changedByUserId: cmd.hostUserId,
    previousRole: previous.role,
    newRole: after.role,
    changedAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);

  const dto = participantSnapshotToDto(after);
  deps.emitter.emitToMeeting({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    event: MeetingSocketEvents.ParticipantChanged,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: now.toISOString(),
      payload: { meetingId: cmd.meetingId, participant: dto, sequence: 0 },
    },
  });

  logHostAction({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    action: direction === 'promote' ? 'meeting.promote_cohost' : 'meeting.demote_cohost',
    actorUserId: cmd.hostUserId,
    targetUserId: cmd.targetUserId,
    correlationId: cmd.correlationId,
    metadata: { previousRole: previous.role, newRole: after.role },
  });

  return Result.ok({ participant: dto });
}

export function promoteParticipantToCohost(
  cmd: ChangeParticipantRoleCommand,
  deps: ChangeParticipantRoleDeps,
): Promise<Result<{ participant: MeetingParticipantDto }>> {
  return changeRole(cmd, 'promote', deps);
}

export function demoteCohostToAttendee(
  cmd: ChangeParticipantRoleCommand,
  deps: ChangeParticipantRoleDeps,
): Promise<Result<{ participant: MeetingParticipantDto }>> {
  return changeRole(cmd, 'demote', deps);
}
