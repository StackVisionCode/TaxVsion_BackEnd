import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import {
  MeetingEventTypes,
  type MeetingParticipantAdmittedEvent,
  type MeetingParticipantRemovedByHostEvent,
  type MeetingLockedEvent,
  type MeetingUnlockedEvent,
  type MeetingHostTransferredEvent,
} from '../../contracts/events/meeting-events.js';
import type { MeetingParticipantDto } from '../../contracts/socket/meeting-socket-events.js';
import { participantSnapshotToDto } from './meeting-mappers.js';

export interface AdmitCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly targetUserId: string;
}

export async function admitParticipant(
  cmd: AdmitCommand,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher },
): Promise<Result<MeetingParticipantDto>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const result = meeting.admit({ hostUserId: cmd.hostUserId, targetUserId: cmd.targetUserId });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const now = new Date();
  const event: MeetingParticipantAdmittedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.ParticipantAdmitted,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: cmd.meetingId,
    participantUserId: cmd.targetUserId,
    admittedByUserId: cmd.hostUserId,
    admittedAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);

  const target = meeting.toSnapshot().participants.find((p) => p.userId === cmd.targetUserId);
  if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target vanished after admit.'));
  return Result.ok(participantSnapshotToDto(target));
}

export interface HostSingleTargetCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly targetUserId: string;
}

export async function removeParticipant(
  cmd: HostSingleTargetCommand,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher },
): Promise<Result<MeetingParticipantDto>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const result = meeting.removeParticipant({
    hostUserId: cmd.hostUserId,
    targetUserId: cmd.targetUserId,
  });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const now = new Date();
  const event: MeetingParticipantRemovedByHostEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.ParticipantRemovedByHost,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: cmd.meetingId,
    removedParticipantUserId: cmd.targetUserId,
    removedByUserId: cmd.hostUserId,
    removedAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);

  const target = meeting.toSnapshot().participants.find((p) => p.userId === cmd.targetUserId);
  if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target vanished after remove.'));
  return Result.ok(participantSnapshotToDto(target));
}

export interface LockCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly locked: boolean;
}

export async function setMeetingLocked(
  cmd: LockCommand,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ isLocked: boolean }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const result = meeting.setLocked({ hostUserId: cmd.hostUserId, locked: cmd.locked });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const now = new Date();
  if (cmd.locked) {
    const event: MeetingLockedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.Locked,
      tenantId: cmd.tenantId,
      correlationId: cmd.correlationId,
      occurredOnUtc: now.toISOString(),
      meetingId: cmd.meetingId,
      lockedByUserId: cmd.hostUserId,
      lockedAtUtc: now.toISOString(),
    };
    await deps.publisher.enqueue(event);
  } else {
    const event: MeetingUnlockedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.Unlocked,
      tenantId: cmd.tenantId,
      correlationId: cmd.correlationId,
      occurredOnUtc: now.toISOString(),
      meetingId: cmd.meetingId,
      unlockedByUserId: cmd.hostUserId,
      unlockedAtUtc: now.toISOString(),
    };
    await deps.publisher.enqueue(event);
  }

  return Result.ok({ isLocked: cmd.locked });
}

export interface MuteAllCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
}

export async function muteAllInMeeting(
  cmd: MuteAllCommand,
  deps: { meetings: MeetingRepository },
): Promise<Result<{ affected: number }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const result = meeting.muteAll({ hostUserId: cmd.hostUserId });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);
  return Result.ok({ affected: meeting.getJoinedParticipants().length - 1 });
}

export interface TransferHostCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly currentHostUserId: string;
  readonly newHostUserId: string;
}

export async function transferMeetingHost(
  cmd: TransferHostCommand,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ hostUserId: string }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const result = meeting.transferHost({
    currentHostUserId: cmd.currentHostUserId,
    newHostUserId: cmd.newHostUserId,
  });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const now = new Date();
  const event: MeetingHostTransferredEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.HostTransferred,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: cmd.meetingId,
    previousHostUserId: cmd.currentHostUserId,
    newHostUserId: cmd.newHostUserId,
    transferredAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);

  return Result.ok({ hostUserId: cmd.newHostUserId });
}
