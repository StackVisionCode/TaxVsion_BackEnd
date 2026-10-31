import type { MeetingParticipantSnapshot } from '../../domain/meetings/meeting-participant.js';
import type { MeetingSnapshot } from '../../domain/meetings/meeting.js';
import type { MeetingParticipantDto, MeetingSnapshotDto } from '../../contracts/socket/meeting-socket-events.js';
import type { AvatarLookup } from './resolve-avatars.js';

/** Sin `avatars` el DTO sale sin `avatarFileId` (el front conserva la foto que ya tenia). */
export function participantSnapshotToDto(
  snap: MeetingParticipantSnapshot,
  avatars?: AvatarLookup,
): MeetingParticipantDto {
  return {
    userId: snap.userId,
    displayName: snap.displayName,
    ...(avatars ? { avatarFileId: avatars.of(snap.userId) } : {}),
    actorType: snap.actorType,
    role: snap.role,
    status: snap.status,
    joinOrder: snap.joinOrder,
    audioEnabled: snap.audioEnabled,
    videoEnabled: snap.videoEnabled,
    screenSharing: snap.screenSharing,
    handRaised: snap.handRaised,
    handRaisedAtUtc: snap.handRaisedAtUtc ? snap.handRaisedAtUtc.toISOString() : null,
    connectionQuality: snap.connectionQuality,
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
  avatars?: AvatarLookup,
): MeetingSnapshotDto {
  return {
    meetingId: snapshot.id,
    status: snapshot.status,
    strategy: snapshot.strategy,
    hostUserId: snapshot.hostUserId,
    isLocked: snapshot.isLocked,
    participants: snapshot.participants.map((p) => participantSnapshotToDto(p, avatars)),
    yourRole,
    sequence: 0,
    conversationId,
    scheduledDurationMinutes: snapshot.scheduledDurationMinutes,
    maxDurationMinutes: snapshot.maxDurationMinutes,
    endsAtUtc: snapshot.endsAtUtc ? snapshot.endsAtUtc.toISOString() : null,
    endReason: snapshot.endReason,
    continuationOfMeetingId: snapshot.continuationOfMeetingId,
  };
}
