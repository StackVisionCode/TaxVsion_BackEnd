import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import { canSeeMeeting, toMeetingListItem } from '../../src/api/http/schemas/meeting-list-item.js';

describe('meeting-list-item (CAL-10)', () => {
  const host = randomUUID();
  const appointmentId = randomUUID();
  const scheduled = Meeting.schedule({
    tenantId: randomUUID(),
    title: 'Consulta inicial',
    host: { userId: host, displayName: 'Carlos' },
    scheduledForUtc: new Date('2026-11-11T13:00:00Z'),
    appointmentId,
  });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  const snapshot = scheduled.value.toSnapshot();

  it('lleva la cita de la sala para que el calendario no la muestre dos veces', () => {
    const item = toMeetingListItem(snapshot, null);
    expect(item.appointmentId).toBe(appointmentId);
    expect(item.scheduledForUtc).toBe('2026-11-11T13:00:00.000Z');
    expect(item.status).toBe('Scheduled');
  });

  it('la ve el host (sin importar mayúsculas) y un invitado vigente; un extraño no', () => {
    const guest = randomUUID();
    const issued = MeetingInvitation.issue({
      meetingId: snapshot.id,
      tenantId: snapshot.tenantId,
      inviteeKind: 'Employee',
      inviteeUserId: guest,
      inviteeEmail: 'andres@example.com',
      ttlSeconds: 3600,
      now: new Date(),
    });
    if (!issued.isSuccess) throw new Error('issue failed');
    const invitations = [issued.value.invitation.toSnapshot()];

    expect(canSeeMeeting(snapshot, host.toUpperCase(), invitations)).toBe(true);
    expect(canSeeMeeting(snapshot, guest, invitations)).toBe(true);
    expect(canSeeMeeting(snapshot, randomUUID(), invitations)).toBe(false);
  });
});
