import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { MeetingSnapshotDto } from '../../contracts/socket/meeting-socket-events.js';
import { buildMeetingSnapshotDto } from './meeting-mappers.js';

/**
 * Re-suscribe a un participante YA admitido a un meeting tras un reconnect transparente del socket
 * (churn del tunnel, sin recargar la página). NO re-hace admisión ni media: es un atajo puro de
 * re-suscripción. El handler socket hace el `socket.join(m:{meetingId})`; este use-case solo valida
 * que el usuario siga siendo participante Joined y devuelve el snapshot para reconciliar la lista de
 * participantes que pudo cambiar durante el corte. Si NO está Joined (sala de espera / removido),
 * falla a propósito: ese caso debe ir por el `join` completo.
 */
export interface RejoinMeetingCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
}

export interface RejoinMeetingResult {
  readonly snapshot: MeetingSnapshotDto;
}

export async function rejoinMeeting(
  cmd: RejoinMeetingCommand,
  deps: { meetings: MeetingRepository },
): Promise<Result<RejoinMeetingResult>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  if (!meeting.isJoinedParticipant(cmd.userId)) {
    return Result.fail(makeError('Meeting.NotJoined', 'You are not an admitted participant of this meeting.'));
  }
  const snapshot = meeting.toSnapshot();
  const me = snapshot.participants.find((p) => p.userId === cmd.userId);
  if (!me) {
    return Result.fail(makeError('Meeting.NotJoined', 'You are not an admitted participant of this meeting.'));
  }
  // conversationId: null a propósito — la room de chat del meeting ya la re-une el join-on-connect,
  // y el cliente conserva el conversationId que ya tenía. Este snapshot solo reconcilia participantes.
  return Result.ok({ snapshot: buildMeetingSnapshotDto(snapshot, me.role, null) });
}
