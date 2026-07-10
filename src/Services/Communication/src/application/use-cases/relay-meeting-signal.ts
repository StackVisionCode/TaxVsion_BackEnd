import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { MeetingSignalDto } from '../../contracts/socket/meeting-socket-events.js';

/**
 * Relay signaling meeting-scoped. Solo participantes joined pueden signal, y
 * el `targetPeerUserId` debe ser tambien participante joined. Cierra CRIT-legacy
 * (§9C del plan): sin broadcast; se despacha al peer exacto.
 */
export interface RelayMeetingSignalCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly fromUserId: string;
  readonly targetPeerUserId: string;
  readonly kind: 'offer' | 'answer' | 'ice';
  readonly data: Record<string, unknown>;
}

export interface RelayMeetingSignalResult {
  readonly targetUserId: string;
  readonly signal: MeetingSignalDto;
}

export async function relayMeetingSignal(
  cmd: RelayMeetingSignalCommand,
  deps: { meetings: MeetingRepository },
): Promise<Result<RelayMeetingSignalResult>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  if (!meeting.isJoinedParticipant(cmd.fromUserId)) {
    return Result.fail(makeError('Meeting.Signal.NotJoined', 'You must be joined to signal.'));
  }
  if (!meeting.isJoinedParticipant(cmd.targetPeerUserId)) {
    return Result.fail(makeError('Meeting.Signal.TargetNotJoined', 'Target peer is not joined.'));
  }
  return Result.ok({
    targetUserId: cmd.targetPeerUserId,
    signal: {
      meetingId: cmd.meetingId,
      fromPeerUserId: cmd.fromUserId,
      kind: cmd.kind,
      data: cmd.data,
    },
  });
}
