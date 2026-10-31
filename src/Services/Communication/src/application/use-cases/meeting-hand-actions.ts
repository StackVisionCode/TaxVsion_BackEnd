import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { MeetingParticipantDto } from '../../contracts/socket/meeting-socket-events.js';
import { participantSnapshotToDto } from './meeting-mappers.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';

/**
 * F5 (D-49): acciones de host sobre las manos. El rol (host/cohost) lo exige el agregado; el permiso
 * `communication.meeting.host` lo exige el handler antes de llegar aqui (permiso != recurso).
 */
export interface LowerHandCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly targetUserId: string;
  readonly correlationId?: string;
}

export interface LowerHandResult {
  readonly changed: boolean;
  readonly participant: MeetingParticipantDto;
}

type Deps = MeetingEventLogDeps & { readonly meetings: MeetingRepository };

export async function lowerParticipantHand(cmd: LowerHandCommand, deps: Deps): Promise<Result<LowerHandResult>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const lowered = meeting.lowerHand({ hostUserId: cmd.hostUserId, targetUserId: cmd.targetUserId });
  if (!lowered.isSuccess) return Result.fail(lowered.error);
  if (lowered.value.changed) {
    await deps.meetings.save(meeting);
    await recordMeetingEvent(deps, {
      tenantId: cmd.tenantId,
      meetingId: cmd.meetingId,
      type: MeetingEventType.HandLoweredByHost,
      actorUserId: cmd.hostUserId,
      targetUserId: cmd.targetUserId,
      correlationId: cmd.correlationId ?? null,
    });
  }
  const target = meeting.toSnapshot().participants.find((p) => p.userId === cmd.targetUserId);
  if (!target) return Result.fail(makeError('Meeting.NotFound', 'Participant vanished.'));
  return Result.ok({ changed: lowered.value.changed, participant: participantSnapshotToDto(target) });
}

export interface LowerAllHandsCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly correlationId?: string;
}

export interface LowerAllHandsResult {
  /** Solo los participantes que tenian la mano levantada (ya bajada). */
  readonly participants: readonly MeetingParticipantDto[];
}

export async function lowerAllHands(cmd: LowerAllHandsCommand, deps: Deps): Promise<Result<LowerAllHandsResult>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const lowered = meeting.lowerAllHands({ hostUserId: cmd.hostUserId });
  if (!lowered.isSuccess) return Result.fail(lowered.error);
  const affected = new Set(lowered.value.lowered);
  if (affected.size > 0) {
    await deps.meetings.save(meeting);
    await recordMeetingEvent(deps, {
      tenantId: cmd.tenantId,
      meetingId: cmd.meetingId,
      type: MeetingEventType.HandLoweredAll,
      actorUserId: cmd.hostUserId,
      payload: { count: affected.size },
      correlationId: cmd.correlationId ?? null,
    });
  }
  const participants = meeting
    .toSnapshot()
    .participants.filter((p) => affected.has(p.userId))
    .map((p) => participantSnapshotToDto(p));
  return Result.ok({ participants });
}
