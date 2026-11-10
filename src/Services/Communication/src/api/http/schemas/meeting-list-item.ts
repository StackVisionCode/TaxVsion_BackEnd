import type { MeetingSnapshot } from '../../../domain/meetings/meeting.js';
import type { MeetingInvitationSnapshot } from '../../../domain/meetings/meeting-invitation.js';

/**
 * Un ítem del listado de reuniones, igual para la lista y para la lectura por id. `appointmentId`
 * marca la sala de una cita de Calendar: el calendario la muestra junto a su cita y no aparte.
 */
export function toMeetingListItem(snapshot: MeetingSnapshot, recordingState: string | null) {
  return {
    id: snapshot.id,
    title: snapshot.title,
    status: snapshot.status,
    shortCode: snapshot.shortCode,
    strategy: snapshot.strategy,
    hostUserId: snapshot.hostUserId,
    appointmentId: snapshot.appointmentId ?? null,
    scheduledForUtc: snapshot.scheduledForUtc?.toISOString() ?? null,
    startedAtUtc: snapshot.startedAtUtc?.toISOString() ?? null,
    endedAtUtc: snapshot.endedAtUtc?.toISOString() ?? null,
    // F3 (T-40): duración programada (informativa), tope, corte y motivo del fin.
    scheduledDurationMinutes: snapshot.scheduledDurationMinutes,
    maxDurationMinutes: snapshot.maxDurationMinutes,
    endsAtUtc: snapshot.endsAtUtc?.toISOString() ?? null,
    endReason: snapshot.endReason,
    durationSeconds: snapshot.durationSeconds,
    continuationOfMeetingId: snapshot.continuationOfMeetingId,
    // En vivo: quienes están ahora; terminada: quienes llegaron a entrar (todos quedan en Left).
    joinedParticipantsCount: snapshot.participants.filter((p) =>
      snapshot.status === 'Live' ? p.status === 'Joined' : p.joinedAtUtc !== null,
    ).length,
    // El historial necesita saber si hay transcript sin abrir cada reunión.
    transcriptFileId: snapshot.transcriptFileId,
    transcriptLanguage: snapshot.transcriptLanguage,
    recordingState,
  };
}

/** La ve quien la ve en su listado: el host, un participante o un invitado vigente. Ids sin importar mayúsculas. */
export function canSeeMeeting(
  snapshot: MeetingSnapshot,
  userId: string,
  invitations: readonly MeetingInvitationSnapshot[],
): boolean {
  const me = userId.toLowerCase();
  const same = (id: string | null | undefined) => !!id && id.toLowerCase() === me;
  return (
    same(snapshot.hostUserId) ||
    snapshot.participants.some((p) => same(p.userId)) ||
    invitations.some((i) => same(i.inviteeUserId) && !i.revokedAtUtc)
  );
}
