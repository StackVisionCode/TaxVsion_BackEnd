import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';

function u(): string {
  return randomUUID();
}
function scheduled(opts: { waitingRoom?: boolean; max?: number; passcodeHash?: string | null } = {}) {
  const host = { userId: u(), displayName: 'Host' };
  const r = Meeting.schedule({
    tenantId: u(),
    title: 'Kickoff',
    host,
    ...(opts.waitingRoom !== undefined ? { requireWaitingRoom: opts.waitingRoom } : {}),
    ...(opts.max !== undefined ? { maxParticipants: opts.max } : {}),
    ...(opts.passcodeHash !== undefined ? { passcodeHash: opts.passcodeHash } : {}),
  });
  if (!r.isSuccess) throw new Error();
  return { meeting: r.value, host };
}

describe('Meeting.schedule', () => {
  it('creates a Scheduled meeting with host as participant', () => {
    const { meeting, host } = scheduled();
    const snap = meeting.toSnapshot();
    expect(snap.status).toBe('Scheduled');
    expect(snap.participants).toHaveLength(1);
    expect(snap.participants[0]!.userId).toBe(host.userId);
    expect(snap.participants[0]!.role).toBe('Host');
    expect(snap.strategy).toBe('Mesh');
    expect(snap.shortCode).toHaveLength(9);
  });

  it('picks SFU strategy when maxParticipants > 4', () => {
    const { meeting } = scheduled({ max: 6 });
    expect(meeting.toSnapshot().strategy).toBe('Sfu');
  });

  it('rejects invalid maxParticipants', () => {
    const r = Meeting.schedule({
      tenantId: u(),
      title: 'X',
      host: { userId: u(), displayName: 'H' },
      maxParticipants: 1,
    });
    expect(r.isSuccess).toBe(false);
  });
});

describe('Meeting.start / end', () => {
  it('only host can start', () => {
    const { meeting } = scheduled();
    expect(meeting.start({ hostUserId: u() }).isSuccess).toBe(false);
  });

  it('start marks Live and admits host', () => {
    const { meeting, host } = scheduled();
    const r = meeting.start({ hostUserId: host.userId });
    expect(r.isSuccess).toBe(true);
    expect(meeting.status).toBe('Live');
    const hostP = meeting.toSnapshot().participants.find((p) => p.userId === host.userId)!;
    expect(hostP.status).toBe('Joined');
  });

  it('end from non-host/cohost is rejected', () => {
    const { meeting, host } = scheduled();
    meeting.start({ hostUserId: host.userId });
    const r = meeting.end({ byUserId: u() });
    expect(r.isSuccess).toBe(false);
  });

  it('cannot end already-ended meeting', () => {
    const { meeting, host } = scheduled();
    meeting.start({ hostUserId: host.userId });
    meeting.end({ byUserId: host.userId });
    const r2 = meeting.end({ byUserId: host.userId });
    expect(r2.isSuccess).toBe(false);
  });
});

describe('Meeting.requestJoin — waiting room + admit', () => {
  it('non-host joins as Waiting when waiting-room enabled', () => {
    const { meeting, host } = scheduled({ waitingRoom: true });
    meeting.start({ hostUserId: host.userId });
    const attendee = u();
    const r = meeting.requestJoin({
      userId: attendee,
      displayName: 'A',
      hasValidInvitation: false,
      passcodeMatch: null,
    });
    expect(r.isSuccess).toBe(true);
    if (!r.isSuccess) return;
    expect(r.value.requiresAdmission).toBe(true);
    expect(r.value.role).toBe('Attendee');
  });

  it('host can admit a waiting participant', () => {
    const { meeting, host } = scheduled({ waitingRoom: true });
    meeting.start({ hostUserId: host.userId });
    const attendee = u();
    meeting.requestJoin({
      userId: attendee,
      displayName: 'A',
      hasValidInvitation: false,
      passcodeMatch: null,
    });
    const admit = meeting.admit({ hostUserId: host.userId, targetUserId: attendee });
    expect(admit.isSuccess).toBe(true);
    const p = meeting.toSnapshot().participants.find((x) => x.userId === attendee)!;
    expect(p.status).toBe('Joined');
  });

  it('rejects locked meeting without invitation', () => {
    const { meeting, host } = scheduled({ waitingRoom: true });
    meeting.start({ hostUserId: host.userId });
    meeting.setLocked({ hostUserId: host.userId, locked: true });
    const r = meeting.requestJoin({
      userId: u(),
      displayName: 'A',
      hasValidInvitation: false,
      passcodeMatch: null,
    });
    expect(r.isSuccess).toBe(false);
    if (r.isSuccess) return;
    expect(r.error.code).toBe('Meeting.Locked');
  });

  it('rejects when full (excluding host)', () => {
    const { meeting, host } = scheduled({ waitingRoom: false, max: 2 });
    meeting.start({ hostUserId: host.userId });
    meeting.requestJoin({ userId: u(), displayName: 'A', hasValidInvitation: false, passcodeMatch: null });
    const r = meeting.requestJoin({
      userId: u(),
      displayName: 'B',
      hasValidInvitation: false,
      passcodeMatch: null,
    });
    expect(r.isSuccess).toBe(false);
    if (r.isSuccess) return;
    expect(r.error.code).toBe('Meeting.Full');
  });

  it('rejects wrong passcode', () => {
    const { meeting, host } = scheduled({ passcodeHash: 'hash', waitingRoom: false });
    meeting.start({ hostUserId: host.userId });
    const r = meeting.requestJoin({
      userId: u(),
      displayName: 'A',
      hasValidInvitation: false,
      passcodeMatch: false,
    });
    expect(r.isSuccess).toBe(false);
    if (r.isSuccess) return;
    expect(r.error.code).toBe('Meeting.InvalidPasscode');
  });
});

describe('Meeting.transferHost', () => {
  it('transfers host to a joined cohost', () => {
    const { meeting, host } = scheduled();
    meeting.start({ hostUserId: host.userId });
    const co = u();
    meeting.requestJoin({ userId: co, displayName: 'Co', hasValidInvitation: false, passcodeMatch: null });
    meeting.admit({ hostUserId: host.userId, targetUserId: co });
    meeting.promoteToCohost({ hostUserId: host.userId, targetUserId: co });
    const r = meeting.transferHost({ currentHostUserId: host.userId, newHostUserId: co });
    expect(r.isSuccess).toBe(true);
    expect(meeting.hostUserId).toBe(co);
  });

  it('rejects transfer to non-joined user', () => {
    const { meeting, host } = scheduled();
    meeting.start({ hostUserId: host.userId });
    const r = meeting.transferHost({ currentHostUserId: host.userId, newHostUserId: u() });
    expect(r.isSuccess).toBe(false);
  });
});

describe('Meeting.muteAll', () => {
  it('mutes everyone except the host', () => {
    const { meeting, host } = scheduled({ waitingRoom: false });
    meeting.start({ hostUserId: host.userId });
    const other = u();
    meeting.requestJoin({ userId: other, displayName: 'Other', hasValidInvitation: false, passcodeMatch: null });
    meeting.muteAll({ hostUserId: host.userId });
    const otherP = meeting.toSnapshot().participants.find((p) => p.userId === other)!;
    expect(otherP.audioEnabled).toBe(false);
  });
});

describe('MeetingInvitation', () => {
  it('issues an opaque token and stores only its SHA-256', () => {
    const r = MeetingInvitation.issue({
      meetingId: u(),
      tenantId: u(),
      inviteeEmail: 'a@b.com',
      ttlSeconds: 3600,
      now: new Date(),
    });
    expect(r.isSuccess).toBe(true);
    if (!r.isSuccess) return;
    expect(r.value.plainToken).toHaveLength(64);
    expect(r.value.invitation.toSnapshot().tokenHash).toBe(MeetingInvitation.hash(r.value.plainToken));
    expect(r.value.invitation.toSnapshot().tokenHash).not.toBe(r.value.plainToken);
  });

  it('validates matching plain token before expiration', () => {
    const now = new Date();
    const r = MeetingInvitation.issue({
      meetingId: u(),
      tenantId: u(),
      inviteeEmail: 'a@b.com',
      ttlSeconds: 3600,
      now,
    });
    if (!r.isSuccess) throw new Error();
    const valid = r.value.invitation.validateForUse({ plainToken: r.value.plainToken, now });
    expect(valid.isSuccess).toBe(true);
    const bad = r.value.invitation.validateForUse({ plainToken: 'x'.repeat(64), now });
    expect(bad.isSuccess).toBe(false);
  });

  it('rejects expired invitation', () => {
    const r = MeetingInvitation.issue({
      meetingId: u(),
      tenantId: u(),
      inviteeEmail: 'a@b.com',
      ttlSeconds: 1,
      now: new Date(Date.now() - 3600 * 1000),
    });
    if (!r.isSuccess) throw new Error();
    const bad = r.value.invitation.validateForUse({ plainToken: r.value.plainToken, now: new Date() });
    expect(bad.isSuccess).toBe(false);
    if (bad.isSuccess) return;
    expect(bad.error.code).toBe('Meeting.Invitation.Expired');
  });
});
