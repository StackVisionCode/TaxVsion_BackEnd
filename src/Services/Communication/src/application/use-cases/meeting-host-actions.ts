import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { MeetingParticipantDto } from '../../contracts/socket/meeting-socket-events.js';
import { participantSnapshotToDto } from './meeting-mappers.js';

export interface AdmitCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly targetUserId: string;
}

export async function admitParticipant(
  cmd: AdmitCommand,
  deps: { meetings: MeetingRepository },
): Promise<Result<MeetingParticipantDto>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const result = meeting.admit({ hostUserId: cmd.hostUserId, targetUserId: cmd.targetUserId });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const target = meeting.toSnapshot().participants.find((p) => p.userId === cmd.targetUserId);
  if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target vanished after admit.'));
  return Result.ok(participantSnapshotToDto(target));
}

export interface HostSingleTargetCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly targetUserId: string;
}

export async function removeParticipant(
  cmd: HostSingleTargetCommand,
  deps: { meetings: MeetingRepository },
): Promise<Result<MeetingParticipantDto>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const result = meeting.removeParticipant({
    hostUserId: cmd.hostUserId,
    targetUserId: cmd.targetUserId,
  });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);
  const target = meeting.toSnapshot().participants.find((p) => p.userId === cmd.targetUserId);
  if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target vanished after remove.'));
  return Result.ok(participantSnapshotToDto(target));
}

export interface LockCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly locked: boolean;
}

export async function setMeetingLocked(
  cmd: LockCommand,
  deps: { meetings: MeetingRepository },
): Promise<Result<{ isLocked: boolean }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const result = meeting.setLocked({ hostUserId: cmd.hostUserId, locked: cmd.locked });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);
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
  readonly meetingId: string;
  readonly currentHostUserId: string;
  readonly newHostUserId: string;
}

export async function transferMeetingHost(
  cmd: TransferHostCommand,
  deps: { meetings: MeetingRepository },
): Promise<Result<{ hostUserId: string }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const result = meeting.transferHost({
    currentHostUserId: cmd.currentHostUserId,
    newHostUserId: cmd.newHostUserId,
  });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);
  return Result.ok({ hostUserId: cmd.newHostUserId });
}
