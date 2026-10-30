import { Result, makeError } from '../../domain/shared/result.js';
import { makeMediaStatus } from '../../domain/calls/media-status.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { MeetingParticipantDto } from '../../contracts/socket/meeting-socket-events.js';
import { participantSnapshotToDto } from './meeting-mappers.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';

export interface UpdateMeetingMediaStatusCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly actorUserId: string;
  readonly audioEnabled: boolean;
  readonly videoEnabled: boolean;
  readonly screenSharing: boolean;
}

export async function updateMeetingMediaStatus(
  cmd: UpdateMeetingMediaStatusCommand,
  deps: MeetingEventLogDeps & { meetings: MeetingRepository },
): Promise<Result<MeetingParticipantDto>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const before = meeting.toSnapshot().participants.find((p) => p.userId === cmd.actorUserId);
  const statusResult = makeMediaStatus({
    audioEnabled: cmd.audioEnabled,
    videoEnabled: cmd.videoEnabled,
    screenSharing: cmd.screenSharing,
  });
  if (!statusResult.isSuccess) return Result.fail(statusResult.error);
  const applied = meeting.applyMediaStatus({ byUserId: cmd.actorUserId, status: statusResult.value });
  if (!applied.isSuccess) return Result.fail(applied.error);
  await deps.meetings.save(meeting);
  const target = meeting.toSnapshot().participants.find((p) => p.userId === cmd.actorUserId);
  if (!target) return Result.fail(makeError('Meeting.NotFound', 'Participant vanished.'));
  // Pantalla compartida con tipo propio (la composición de F7 la necesita aparte); el resto, media.changed.
  const screenChanged = before !== undefined && before.screenSharing !== target.screenSharing;
  await recordMeetingEvent(deps, {
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    type: screenChanged
      ? target.screenSharing
        ? MeetingEventType.ScreenStarted
        : MeetingEventType.ScreenStopped
      : MeetingEventType.MediaChanged,
    actorUserId: cmd.actorUserId,
    payload: { audioEnabled: target.audioEnabled, videoEnabled: target.videoEnabled, screenSharing: target.screenSharing },
  });
  return Result.ok(participantSnapshotToDto(target));
}

export interface RaiseHandCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly actorUserId: string;
  readonly raised: boolean;
}

export async function updateRaiseHand(
  cmd: RaiseHandCommand,
  deps: MeetingEventLogDeps & { meetings: MeetingRepository },
): Promise<Result<MeetingParticipantDto>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const applied = meeting.raiseHand({ byUserId: cmd.actorUserId, raised: cmd.raised });
  if (!applied.isSuccess) return Result.fail(applied.error);
  await deps.meetings.save(meeting);
  const target = meeting.toSnapshot().participants.find((p) => p.userId === cmd.actorUserId);
  if (!target) return Result.fail(makeError('Meeting.NotFound', 'Participant vanished.'));
  await recordMeetingEvent(deps, {
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    type: cmd.raised ? MeetingEventType.HandRaised : MeetingEventType.HandLowered,
    actorUserId: cmd.actorUserId,
  });
  return Result.ok(participantSnapshotToDto(target));
}
