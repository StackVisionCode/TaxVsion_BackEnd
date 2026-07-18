import type { MeetingParticipantSnapshot } from '../../domain/meetings/meeting-participant.js';
import type { MeetingParticipantDto } from '../../contracts/socket/meeting-socket-events.js';

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
