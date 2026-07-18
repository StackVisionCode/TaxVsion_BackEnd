import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
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
import { ensureMeetingConversation, removeFromMeetingConversation } from './ensure-meeting-conversation.js';
import { logHostAction } from './host-audit.js';

export interface AdmitCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly targetUserId: string;
}

export interface AdmitResult {
  readonly participant: MeetingParticipantDto;
  readonly conversationId: string | null;
}

export async function admitParticipant(
  cmd: AdmitCommand,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher; conversations: ConversationRepository },
): Promise<Result<AdmitResult>> {
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
  logHostAction({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    action: 'meeting.admit',
    actorUserId: cmd.hostUserId,
    targetUserId: cmd.targetUserId,
    correlationId: cmd.correlationId,
  });

  const snapshot = meeting.toSnapshot();
  const target = snapshot.participants.find((p) => p.userId === cmd.targetUserId);
  if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target vanished after admit.'));

  // Best-effort — un fallo aca no debe deshacer la admision ya confirmada.
  const chatResult = await ensureMeetingConversation(
    {
      tenantId: cmd.tenantId,
      correlationId: cmd.correlationId,
      meetingId: cmd.meetingId,
      meetingTitle: snapshot.title,
      member: { userId: target.userId, displayName: target.displayName, actorType: 'TenantEmployee' },
    },
    deps,
  );

  return Result.ok({
    participant: participantSnapshotToDto(target),
    conversationId: chatResult.isSuccess ? chatResult.value.conversationId : null,
  });
}

export interface HostSingleTargetCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly targetUserId: string;
}

export interface RemoveParticipantResult {
  readonly participant: MeetingParticipantDto;
  readonly conversationId: string | null;
}

export async function removeParticipant(
  cmd: HostSingleTargetCommand,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher; conversations: ConversationRepository },
): Promise<Result<RemoveParticipantResult>> {
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
  logHostAction({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    action: 'meeting.remove',
    actorUserId: cmd.hostUserId,
    targetUserId: cmd.targetUserId,
    correlationId: cmd.correlationId,
  });

  const target = meeting.toSnapshot().participants.find((p) => p.userId === cmd.targetUserId);
  if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target vanished after remove.'));

  // Best-effort — la expulsion del meeting ya se confirmo; un fallo aca solo
  // deja al expulsado con acceso de lectura al chat historico, no rompe nada.
  const chatResult = await removeFromMeetingConversation(
    { tenantId: cmd.tenantId, meetingId: cmd.meetingId, actorUserId: cmd.hostUserId, targetUserId: cmd.targetUserId },
    deps,
  ).catch(() => null);

  return Result.ok({
    participant: participantSnapshotToDto(target),
    conversationId: chatResult?.isSuccess && chatResult.value ? chatResult.value.conversationId : null,
  });
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

  logHostAction({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    action: cmd.locked ? 'meeting.lock' : 'meeting.unlock',
    actorUserId: cmd.hostUserId,
    correlationId: cmd.correlationId,
  });

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
  const affected = meeting.getJoinedParticipants().length - 1;
  logHostAction({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    action: 'meeting.mute_all',
    actorUserId: cmd.hostUserId,
    metadata: { affected },
  });
  return Result.ok({ affected });
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
  logHostAction({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    action: 'meeting.transfer_host',
    actorUserId: cmd.currentHostUserId,
    targetUserId: cmd.newHostUserId,
    correlationId: cmd.correlationId,
  });

  return Result.ok({ hostUserId: cmd.newHostUserId });
}
