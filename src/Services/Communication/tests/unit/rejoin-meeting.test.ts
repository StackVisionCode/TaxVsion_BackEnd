import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import { rejoinMeeting } from '../../src/application/use-cases/rejoin-meeting.js';

const u = (): string => randomUUID();

/** Meeting en vivo con un attendee admitido (Joined), mismo patrón que meeting-recording-flow.test. */
function liveMeetingWithAttendee(): { meeting: Meeting; attendeeUserId: string } {
  const host = { userId: u(), displayName: 'Host' };
  const attendeeUserId = u();
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Consulta', host });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  meeting.requestJoin({ userId: attendeeUserId, displayName: 'Cliente', hasValidInvitation: false, passcodeMatch: null });
  meeting.admit({ hostUserId: host.userId, targetUserId: attendeeUserId });
  return { meeting, attendeeUserId };
}

// Solo se usa findById; el resto del puerto no interviene en rejoin.
function repoWith(meeting: Meeting | null): MeetingRepository {
  return { findById: async () => meeting } as unknown as MeetingRepository;
}

describe('rejoinMeeting', () => {
  it('re-une a un participante admitido y devuelve el snapshot con la lista de participantes', async () => {
    const { meeting, attendeeUserId } = liveMeetingWithAttendee();
    const result = await rejoinMeeting(
      { tenantId: meeting.tenantId, meetingId: meeting.id, userId: attendeeUserId },
      { meetings: repoWith(meeting) },
    );
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value.snapshot.meetingId).toBe(meeting.id);
      expect(result.value.snapshot.participants.some((p) => p.userId === attendeeUserId)).toBe(true);
      // conversationId null a propósito: el cliente conserva el suyo (ver rejoin-meeting.ts).
      expect(result.value.snapshot.conversationId).toBeNull();
    }
  });

  it('falla Meeting.NotJoined si el usuario no es participante admitido', async () => {
    const { meeting } = liveMeetingWithAttendee();
    const result = await rejoinMeeting(
      { tenantId: meeting.tenantId, meetingId: meeting.id, userId: u() },
      { meetings: repoWith(meeting) },
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) {
      expect(result.error.code).toBe('Meeting.NotJoined');
    }
  });

  it('falla Meeting.NotFound si el meeting no existe', async () => {
    const result = await rejoinMeeting({ tenantId: u(), meetingId: u(), userId: u() }, { meetings: repoWith(null) });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) {
      expect(result.error.code).toBe('Meeting.NotFound');
    }
  });
});
