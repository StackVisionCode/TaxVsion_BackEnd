import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';

function u(): string {
  return randomUUID();
}

function liveMeetingWithWaitingAttendee() {
  const host = { userId: u(), displayName: 'Host' };
  const attendeeUserId = u();
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Consulta', host });
  if (!scheduled.isSuccess) throw new Error();
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  meeting.requestJoin({ userId: attendeeUserId, displayName: 'Cliente', hasValidInvitation: false, passcodeMatch: null });
  return { meeting, host, attendeeUserId };
}

describe('Meeting.denyWaitingRoom', () => {
  it('moves a Waiting participant to Removed', () => {
    const { meeting, host, attendeeUserId } = liveMeetingWithWaitingAttendee();
    const result = meeting.denyWaitingRoom({ hostUserId: host.userId, targetUserId: attendeeUserId });
    expect(result.isSuccess).toBe(true);
    const attendee = meeting.toSnapshot().participants.find((p) => p.userId === attendeeUserId);
    expect(attendee?.status).toBe('Removed');
  });

  it('rejects if target is not currently Waiting', () => {
    const { meeting, host, attendeeUserId } = liveMeetingWithWaitingAttendee();
    meeting.admit({ hostUserId: host.userId, targetUserId: attendeeUserId });
    const result = meeting.denyWaitingRoom({ hostUserId: host.userId, targetUserId: attendeeUserId });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.NotWaiting');
  });

  it('rejects if actor is not host/cohost', () => {
    const { meeting, attendeeUserId } = liveMeetingWithWaitingAttendee();
    const result = meeting.denyWaitingRoom({ hostUserId: u(), targetUserId: attendeeUserId });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.HostOnly');
  });
});

describe('Meeting.reschedule', () => {
  it('changes scheduledForUtc while Scheduled', () => {
    const host = { userId: u(), displayName: 'Host' };
    const scheduled = Meeting.schedule({
      tenantId: u(),
      title: 'Consulta',
      host,
      scheduledForUtc: new Date('2026-08-01T10:00:00Z'),
    });
    if (!scheduled.isSuccess) throw new Error();
    const meeting = scheduled.value;
    const newDate = new Date('2026-08-05T15:00:00Z');
    const result = meeting.reschedule({ hostUserId: host.userId, newScheduledForUtc: newDate });
    expect(result.isSuccess).toBe(true);
    expect(meeting.toSnapshot().scheduledForUtc?.toISOString()).toBe(newDate.toISOString());
  });

  it('accepts null to un-schedule', () => {
    const host = { userId: u(), displayName: 'Host' };
    const scheduled = Meeting.schedule({ tenantId: u(), title: 'x', host, scheduledForUtc: new Date() });
    if (!scheduled.isSuccess) throw new Error();
    const result = scheduled.value.reschedule({ hostUserId: host.userId, newScheduledForUtc: null });
    expect(result.isSuccess).toBe(true);
    expect(scheduled.value.toSnapshot().scheduledForUtc).toBeNull();
  });

  it('rejects rescheduling after start (only Scheduled → editable)', () => {
    const host = { userId: u(), displayName: 'Host' };
    const scheduled = Meeting.schedule({ tenantId: u(), title: 'x', host });
    if (!scheduled.isSuccess) throw new Error();
    const meeting = scheduled.value;
    meeting.start({ hostUserId: host.userId });
    const result = meeting.reschedule({ hostUserId: host.userId, newScheduledForUtc: new Date() });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.InvalidTransition');
  });

  it('rejects if actor is not host/cohost', () => {
    const host = { userId: u(), displayName: 'Host' };
    const scheduled = Meeting.schedule({ tenantId: u(), title: 'x', host });
    if (!scheduled.isSuccess) throw new Error();
    const result = scheduled.value.reschedule({ hostUserId: u(), newScheduledForUtc: new Date() });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.HostOnly');
  });
});

describe('Meeting.cancel (Fase 6 recall — only Scheduled, host-only)', () => {
  it('cancels a Scheduled meeting', () => {
    const host = { userId: u(), displayName: 'Host' };
    const scheduled = Meeting.schedule({ tenantId: u(), title: 'x', host });
    if (!scheduled.isSuccess) throw new Error();
    const result = scheduled.value.cancel({ hostUserId: host.userId });
    expect(result.isSuccess).toBe(true);
    expect(scheduled.value.status).toBe('Cancelled');
  });

  it('cannot cancel a Live meeting (must be ended, not cancelled)', () => {
    const host = { userId: u(), displayName: 'Host' };
    const scheduled = Meeting.schedule({ tenantId: u(), title: 'x', host });
    if (!scheduled.isSuccess) throw new Error();
    const meeting = scheduled.value;
    meeting.start({ hostUserId: host.userId });
    const result = meeting.cancel({ hostUserId: host.userId });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.InvalidTransition');
  });
});
