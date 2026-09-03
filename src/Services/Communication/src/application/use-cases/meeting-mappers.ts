import type { MeetingParticipantSnapshot } from '../../domain/meetings/meeting-participant.js';
import type { MeetingSnapshot } from '../../domain/meetings/meeting.js';
import type { MeetingParticipantDto, MeetingSnapshotDto } from '../../contracts/socket/meeting-socket-events.js';

export function participantSnapshotToDto(snap: MeetingParticipantSnapshot): MeetingParticipantDto {
  return {
    userId: snap.userId,
    displayName: snap.displayName,
    role: snap.role,
    status: snap.status,
    joinOrder: snap.joinOrder,
    audioEnabled: snap.audioEnabled,
    videoEnabled: snap.videoEnabled,
    screenSharing: snap.screenSharing,
    handRaised: snap.handRaised,
  };
}

/**
 * Arma el `MeetingSnapshotDto` de un usuario concreto (su `yourRole`/`conversationId`).
 * Compartido por el join (ack) y por la admisión (evento `meeting.snapshot` al
 * admitido) — el snapshot es la única forma en que el cliente conoce la lista de
 * participantes + conversationId + estrategia.
 */
export function buildMeetingSnapshotDto(
  snapshot: MeetingSnapshot,
  yourRole: MeetingSnapshotDto['yourRole'],
  conversationId: string | null,
): MeetingSnapshotDto {
  return {
    meetingId: snapshot.id,
    status: snapshot.status,
    strategy: snapshot.strategy,
    hostUserId: snapshot.hostUserId,
    isLocked: snapshot.isLocked,
    participants: snapshot.participants.map(participantSnapshotToDto),
    yourRole,
    sequence: 0,
    conversationId,
  };
}
