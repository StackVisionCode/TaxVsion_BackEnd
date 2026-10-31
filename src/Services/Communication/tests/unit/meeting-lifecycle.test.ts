import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';

const u = (): string => randomUUID();
const MIN = 60_000;

function scheduled(opts: { durationPolicy?: { defaultDurationMinutes: number; maxDurationMinutes: number }; scheduledDurationMinutes?: number } = {}) {
  const host = { userId: u(), displayName: 'Host' };
  const r = Meeting.schedule({ tenantId: u(), title: 'Kickoff', host, requireWaitingRoom: false, ...opts });
  if (!r.isSuccess) throw new Error(r.error.message);
  return { meeting: r.value, host };
}

function live(opts: { maxDurationMinutes?: number; now?: Date } = {}) {
  const { meeting, host } = scheduled();
  const now = opts.now ?? new Date('2026-10-29T10:00:00Z');
  const started = meeting.start({ hostUserId: host.userId, maxDurationMinutes: opts.maxDurationMinutes ?? 60, now });
  if (!started.isSuccess) throw new Error(started.error.message);
  return { meeting, host, now };
}

function join(meeting: Meeting, actorType: string, now: Date): string {
  const userId = u();
  const r = meeting.requestJoin({ userId, displayName: actorType, actorType, hasValidInvitation: false, passcodeMatch: null, now });
  if (!r.isSuccess) throw new Error(r.error.message);
  return userId;
}

describe('Meeting duration (D-34/D-35)', () => {
  it('schedule takes the office default and rejects a duration above the office max', () => {
    const policy = { defaultDurationMinutes: 45, maxDurationMinutes: 60 };
    expect(scheduled({ durationPolicy: policy }).meeting.toSnapshot().scheduledDurationMinutes).toBe(45);
    const r = Meeting.schedule({ tenantId: u(), title: 'x', host: { userId: u(), displayName: 'h' }, durationPolicy: policy, scheduledDurationMinutes: 90 });
    expect(!r.isSuccess && r.error.code).toBe('Meeting.InvalidDuration');
  });

  it('start cuts at the chosen duration when it is shorter than the office max (D-35 revised)', () => {
    const { meeting, host } = scheduled({ scheduledDurationMinutes: 15 });
    const now = new Date('2026-10-31T20:00:00Z');
    expect(meeting.start({ hostUserId: host.userId, maxDurationMinutes: 60, now }).isSuccess).toBe(true);
    const snap = meeting.toSnapshot();
    expect(snap.maxDurationMinutes).toBe(15);
    expect(snap.endsAtUtc?.getTime()).toBe(now.getTime() + 15 * MIN);
  });

  it('if the office max was lowered after scheduling, the max wins at start', () => {
    const { meeting, host } = scheduled({ scheduledDurationMinutes: 45 });
    const now = new Date('2026-10-31T20:00:00Z');
    meeting.start({ hostUserId: host.userId, maxDurationMinutes: 30, now });
    expect(meeting.toSnapshot().endsAtUtc?.getTime()).toBe(now.getTime() + 30 * MIN);
  });

  it('start never goes past the effective max (scheduled default above a lowered max)', () => {
    const { meeting, now } = live({ maxDurationMinutes: 30 });
    const snap = meeting.toSnapshot();
    expect(snap.maxDurationMinutes).toBe(30);
    expect(snap.endsAtUtc?.getTime()).toBe(now.getTime() + 30 * MIN);
    expect(meeting.remainingSeconds(new Date(now.getTime() + 29 * MIN))).toBe(60);
  });

  it('nobody can join once endsAtUtc passed, even if the scheduler has not cut yet', () => {
    const { meeting, now } = live({ maxDurationMinutes: 10 });
    const late = meeting.requestJoin({ userId: u(), displayName: 'late', hasValidInvitation: false, passcodeMatch: null, now: new Date(now.getTime() + 11 * MIN) });
    expect(!late.isSuccess && late.error.code).toBe('Meeting.Ended');
  });

  it('duration warnings are recorded once per threshold and only descending', () => {
    const { meeting } = live();
    expect(meeting.markDurationWarning({ thresholdSeconds: 600 }).isSuccess).toBe(true);
    expect(meeting.markDurationWarning({ thresholdSeconds: 600 }).isSuccess).toBe(false);
    expect(meeting.markDurationWarning({ thresholdSeconds: 300 }).isSuccess).toBe(true);
    expect(meeting.markDurationWarning({ thresholdSeconds: 600 }).isSuccess).toBe(false);
    expect(meeting.toSnapshot().lastDurationWarningSeconds).toBe(300);
  });

  it('endBySystem keeps the reason; end by host records Host; a terminal meeting cannot end twice', () => {
    const a = live().meeting;
    expect(a.endBySystem({ reason: 'MaxDurationReached' }).isSuccess).toBe(true);
    expect(a.toSnapshot().endReason).toBe('MaxDurationReached');
    expect(!a.endBySystem({ reason: 'NoParticipants' }).isSuccess).toBe(true);
    const { meeting: b, host } = live();
    expect(b.end({ byUserId: host.userId }).isSuccess).toBe(true);
    expect(b.toSnapshot().endReason).toBe('Host');
  });

  it('a Scheduled meeting expires into a terminal state that rejects joins', () => {
    const { meeting } = scheduled();
    expect(meeting.expire().isSuccess).toBe(true);
    expect(meeting.status).toBe('Expired');
    const r = meeting.requestJoin({ userId: u(), displayName: 'x', hasValidInvitation: false, passcodeMatch: null });
    expect(!r.isSuccess && r.error.code).toBe('Meeting.NotJoinable');
    expect(meeting.expire().isSuccess).toBe(false);
  });

  it('emptySinceUtc is null while someone is joined and the latest leave otherwise', () => {
    const { meeting, host, now } = live();
    expect(meeting.emptySinceUtc()).toBeNull();
    const leftAt = new Date(now.getTime() + 5 * MIN);
    meeting.leave({ userId: host.userId, now: leftAt });
    expect(meeting.emptySinceUtc()?.getTime()).toBe(leftAt.getTime());
  });
});

describe('Host succession (D-08, T-06) and eligibility (D-09)', () => {
  it('prefers the joined cohost, then the oldest joined staff member', () => {
    const { meeting, host, now } = live();
    const staffA = join(meeting, 'TenantEmployee', now);
    const staffB = join(meeting, 'TenantEmployee', now);
    const customer = join(meeting, 'CustomerPortal', now);
    expect(meeting.promoteToCohost({ hostUserId: host.userId, targetUserId: staffB }).isSuccess).toBe(true);
    meeting.leave({ userId: host.userId, now });

    const r = meeting.succeedHost({ now });
    expect(r.isSuccess && r.value).toEqual({ outcome: 'succeeded', newHostUserId: staffB });
    expect(meeting.hostUserId).toBe(staffB);
    const roles = Object.fromEntries(meeting.toSnapshot().participants.map((p) => [p.userId, p.role]));
    expect(roles[host.userId]).toBe('Cohost');
    expect(roles[staffA]).toBe('Attendee');
    expect(roles[customer]).toBe('Attendee');
  });

  it('falls back to the oldest staff member and never to a customer or guest', () => {
    const { meeting, host, now } = live();
    join(meeting, 'CustomerPortal', now);
    const staff = join(meeting, 'TenantAdmin', now);
    meeting.leave({ userId: host.userId, now });
    const r = meeting.succeedHost({ now });
    expect(r.isSuccess && r.value).toEqual({ outcome: 'succeeded', newHostUserId: staff });
  });

  it('ends the meeting by NoParticipants when only customers remain', () => {
    const { meeting, host, now } = live();
    join(meeting, 'CustomerPortal', now);
    meeting.leave({ userId: host.userId, now });
    const r = meeting.succeedHost({ now });
    expect(r.isSuccess && r.value.outcome).toBe('ended');
    expect(meeting.status).toBe('Ended');
    expect(meeting.toSnapshot().endReason).toBe('NoParticipants');
  });

  it('refuses succession while the host is still joined', () => {
    const { meeting, now } = live();
    const r = meeting.succeedHost({ now });
    expect(!r.isSuccess && r.error.code).toBe('Meeting.HostPresent');
  });

  it('rejects promoting or transferring to a customer or guest', () => {
    const { meeting, host, now } = live();
    const customer = join(meeting, 'CustomerPortal', now);
    const guest = join(meeting, 'Guest', now);
    const promote = meeting.promoteToCohost({ hostUserId: host.userId, targetUserId: customer });
    expect(!promote.isSuccess && promote.error.code).toBe('Meeting.Role.NotEligible');
    const transfer = meeting.transferHost({ currentHostUserId: host.userId, newHostUserId: guest });
    expect(!transfer.isSuccess && transfer.error.code).toBe('Meeting.Role.NotEligible');
    expect(meeting.hostUserId).toBe(host.userId);
  });

  it('participants keep the customerId they joined with (D-58)', () => {
    const { meeting, now } = live();
    const customerId = u();
    const userId = u();
    meeting.requestJoin({ userId, displayName: 'c', actorType: 'CustomerPortal', customerId, hasValidInvitation: false, passcodeMatch: null, now });
    expect(meeting.toSnapshot().participants.find((p) => p.userId === userId)?.customerId).toBe(customerId);
  });
});

describe('Removed participants (F11, E-259)', () => {
  it('someone the host removed goes to the waiting room when joining again, even without a waiting room', () => {
    const { meeting, host, now } = live();
    const kicked = join(meeting, 'TenantEmployee', now);
    expect(meeting.removeParticipant({ hostUserId: host.userId, targetUserId: kicked, now }).isSuccess).toBe(true);
    const again = meeting.requestJoin({ userId: kicked, displayName: 'TenantEmployee', actorType: 'TenantEmployee', hasValidInvitation: false, passcodeMatch: null, now });
    expect(again.isSuccess && again.value.requiresAdmission).toBe(true);
  });

  it('someone new still joins directly when the meeting has no waiting room', () => {
    const { meeting, now } = live();
    const userId = randomUUID();
    const r = meeting.requestJoin({ userId, displayName: 'New', actorType: 'TenantEmployee', hasValidInvitation: false, passcodeMatch: null, now });
    expect(r.isSuccess && r.value.requiresAdmission).toBe(false);
  });
});
