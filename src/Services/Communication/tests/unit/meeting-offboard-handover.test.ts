import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';

function u(): string {
  return randomUUID();
}

function scheduled(hostUserId: string) {
  const result = Meeting.schedule({
    tenantId: u(),
    title: 'Consulta',
    host: { userId: hostUserId, displayName: 'Leaver' },
    scheduledForUtc: new Date('2026-08-01T10:00:00Z'),
  });
  if (!result.isSuccess) throw new Error('schedule failed');
  return result.value;
}

function live(hostUserId: string) {
  const meeting = scheduled(hostUserId);
  const started = meeting.start({ hostUserId });
  if (!started.isSuccess) throw new Error('start failed');
  return meeting;
}

describe('Meeting.reassignHostBySystem', () => {
  it('gives host to a brand-new successor (placeholder) and stands the leaver down', () => {
    const leaver = u();
    const successor = u();
    const meeting = scheduled(leaver);

    const result = meeting.reassignHostBySystem({
      newHostUserId: successor,
      newHostDisplayName: 'Successor',
    });

    expect(result.isSuccess).toBe(true);
    const snap = meeting.toSnapshot();
    expect(snap.hostUserId).toBe(successor);
    const successorParticipant = snap.participants.find((p) => p.userId === successor);
    expect(successorParticipant?.role).toBe('Host');
    const leaverParticipant = snap.participants.find((p) => p.userId === leaver);
    expect(leaverParticipant?.role).toBe('Attendee');
    expect(leaverParticipant?.status).toBe('Left');
  });

  it('promotes an already-joined participant to host in a live meeting', () => {
    const leaver = u();
    const successor = u();
    const meeting = live(leaver);
    meeting.requestJoin({
      userId: successor,
      displayName: 'Successor',
      hasValidInvitation: true,
      passcodeMatch: null,
    });

    const result = meeting.reassignHostBySystem({ newHostUserId: successor });

    expect(result.isSuccess).toBe(true);
    const snap = meeting.toSnapshot();
    expect(snap.hostUserId).toBe(successor);
    expect(snap.participants.find((p) => p.userId === successor)?.role).toBe('Host');
  });

  it('is a no-op when the new host is already the host', () => {
    const host = u();
    const meeting = scheduled(host);

    const result = meeting.reassignHostBySystem({ newHostUserId: host });

    expect(result.isSuccess).toBe(true);
    expect(meeting.toSnapshot().hostUserId).toBe(host);
  });

  it('cannot reassign the host of an ended meeting', () => {
    const host = u();
    const meeting = live(host);
    meeting.endBySystem();

    const result = meeting.reassignHostBySystem({ newHostUserId: u() });

    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.InvalidTransition');
  });
});

describe('Meeting.cancelBySystem', () => {
  it('cancels a scheduled meeting', () => {
    const meeting = scheduled(u());

    const result = meeting.cancelBySystem();

    expect(result.isSuccess).toBe(true);
    expect(meeting.status).toBe('Cancelled');
  });

  it('refuses to cancel a live meeting (must be ended instead)', () => {
    const host = u();
    const meeting = live(host);

    const result = meeting.cancelBySystem();

    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.InvalidTransition');
  });
});

describe('Meeting.endBySystem', () => {
  it('ends a live meeting and drops everyone', () => {
    const host = u();
    const meeting = live(host);

    const result = meeting.endBySystem();

    expect(result.isSuccess).toBe(true);
    expect(meeting.status).toBe('Ended');
    expect(
      meeting.toSnapshot().participants.every((p) => p.status === 'Left' || p.status === 'Removed'),
    ).toBe(true);
  });

  it('refuses to end an already-ended meeting', () => {
    const host = u();
    const meeting = live(host);
    meeting.endBySystem();

    const result = meeting.endBySystem();

    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.AlreadyEnded');
  });
});
