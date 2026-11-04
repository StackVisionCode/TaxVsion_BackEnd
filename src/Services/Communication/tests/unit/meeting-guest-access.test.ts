import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import { resolveInvitationToken } from '../../src/application/use-cases/resolve-invitation-token.js';
import { getGuestMeetingContext } from '../../src/application/use-cases/get-guest-meeting-context.js';
import { joinMeeting } from '../../src/application/use-cases/join-meeting.js';
import {
  enableMeetingGuestLink,
  setMeetingGuestAccess,
} from '../../src/application/use-cases/meeting-guest-access.js';
import {
  isGuestAccessToken,
  parseGuestLinkToken,
  signGuestLinkToken,
  verifyGuestLinkToken,
} from '../../src/infrastructure/jwks/guest-link-token.js';
import { verifyGuestJoinTicket } from '../../src/infrastructure/jwks/join-ticket.js';
import { buildMeetingRuntime } from '../helpers/fake-meeting-runtime.js';
import type { PasscodeHasher } from '../../src/application/ports/passcode-hasher.js';
import type { TurnCredentialFactory } from '../../src/application/ports/turn-credential-factory.js';

const u = (): string => randomUUID();

function liveMeeting(tenantId = u(), opts: { waitingRoom?: boolean; guestsAllowed?: boolean } = {}) {
  const host = { userId: u(), displayName: 'Ana Host' };
  const scheduled = Meeting.schedule({
    tenantId,
    title: 'Declaracion 2025',
    host,
    requireWaitingRoom: opts.waitingRoom ?? false,
    ...(opts.guestsAllowed === undefined ? {} : { guestsAllowed: opts.guestsAllowed }),
  });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  return { meeting, host };
}

function joinDeps(runtime: ReturnType<typeof buildMeetingRuntime>) {
  return {
    ...runtime.deps,
    passcodes: { verify: async () => true, hash: async () => 'h' } as unknown as PasscodeHasher,
    turn: { issue: () => ({ iceServers: [], expiresAtUtc: new Date().toISOString() }) } as unknown as TurnCredentialFactory,
  };
}

function accessDeps(runtime: ReturnType<typeof buildMeetingRuntime>) {
  return { ...runtime.deps, tenantHostResolver: { resolveHost: async () => 'office.taxoffix.test' } };
}

describe('D-67: an external guest always waits to be admitted', () => {
  it('goes to the waiting room even when the meeting has no waiting room', () => {
    const { meeting } = liveMeeting();
    const joined = meeting.requestJoin({ userId: u(), displayName: 'Pepe', actorType: 'Guest', isGuest: true, hasValidInvitation: true, passcodeMatch: null });
    expect(joined.isSuccess).toBe(true);
    if (joined.isSuccess) expect(joined.value.requiresAdmission).toBe(true);
  });
});

describe('D-69: external guests per meeting', () => {
  it('turning it off rejects waiting guests and new ones, but guests already inside stay', () => {
    const { meeting, host } = liveMeeting();
    const inside = u();
    const waiting = u();
    meeting.requestJoin({ userId: inside, displayName: 'In', actorType: 'Guest', isGuest: true, hasValidInvitation: true, passcodeMatch: null });
    meeting.admit({ hostUserId: host.userId, targetUserId: inside });
    meeting.requestJoin({ userId: waiting, displayName: 'Wait', actorType: 'Guest', isGuest: true, hasValidInvitation: true, passcodeMatch: null });

    const result = meeting.setGuestAccess({ hostUserId: host.userId, allowed: false });

    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) expect(result.value.deniedUserIds).toEqual([waiting]);
    const status = (id: string) => meeting.toSnapshot().participants.find((p) => p.userId === id)?.status;
    expect(status(inside)).toBe('Joined');
    expect(status(waiting)).toBe('Removed');
    const newcomer = meeting.requestJoin({ userId: u(), displayName: 'New', actorType: 'Guest', isGuest: true, hasValidInvitation: true, passcodeMatch: null });
    expect(newcomer.isSuccess).toBe(false);
    if (!newcomer.isSuccess) expect(newcomer.error.code).toBe('Meeting.GuestsNotAllowed');
  });

  it('a meeting scheduled without external guests rejects their invitation link', async () => {
    const runtime = buildMeetingRuntime();
    const { meeting } = liveMeeting(u(), { guestsAllowed: false });
    await runtime.repository.save(meeting);
    const issued = MeetingInvitation.issue({ meetingId: meeting.id, tenantId: meeting.tenantId, inviteeKind: 'External', inviteeEmail: 'guest@example.com', ttlSeconds: 3600, now: new Date() });
    if (!issued.isSuccess) throw new Error();
    await runtime.repository.saveInvitation(issued.value.invitation);

    const resolved = await resolveInvitationToken({ token: issued.value.plainToken }, runtime.deps);

    expect(resolved.isSuccess).toBe(false);
    if (!resolved.isSuccess) expect(resolved.error.code).toBe('Meeting.GuestsNotAllowed');
  });

  it('the office ceiling wins over the meeting option', async () => {
    const runtime = buildMeetingRuntime({ guestsAllowed: false });
    const { meeting, host } = liveMeeting(u(), { guestsAllowed: true });
    await runtime.repository.save(meeting);

    const opened = await setMeetingGuestAccess(
      { tenantId: meeting.tenantId, correlationId: u(), meetingId: meeting.id, actorUserId: host.userId, allowed: true },
      accessDeps(runtime),
    );

    expect(opened.isSuccess).toBe(false);
    if (!opened.isSuccess) expect(opened.error.code).toBe('Meeting.GuestsNotAllowed');
  });
});

describe('D-68: meeting link for external guests', () => {
  it('the token is derived per version: rotating invalidates the previous link', () => {
    const meetingId = u();
    const v1 = signGuestLinkToken(meetingId, 1);
    expect(parseGuestLinkToken(v1)?.meetingId).toBe(meetingId);
    expect(verifyGuestLinkToken(v1, 1)).toBe(true);
    expect(verifyGuestLinkToken(v1, 2)).toBe(false);
    expect(parseGuestLinkToken('a'.repeat(64))).toBeNull();
  });

  it('las rutas publicas aceptan el enlace de la reunion y el token de invitacion (antes solo 64 caracteres)', () => {
    expect(isGuestAccessToken(signGuestLinkToken(u(), 1))).toBe(true);
    expect(isGuestAccessToken('a'.repeat(64))).toBe(true);
    expect(isGuestAccessToken('a'.repeat(63))).toBe(false);
    expect(isGuestAccessToken(`${u()}.short`)).toBe(false);
  });

  it('each person using the link is a different guest who waits to be admitted', async () => {
    const runtime = buildMeetingRuntime();
    const { meeting, host } = liveMeeting();
    await runtime.repository.save(meeting);
    const enabled = await enableMeetingGuestLink(
      { tenantId: meeting.tenantId, correlationId: u(), meetingId: meeting.id, actorUserId: host.userId, rotate: false },
      accessDeps(runtime),
    );
    expect(enabled.isSuccess).toBe(true);
    if (!enabled.isSuccess || !enabled.value.guestLinkUrl) throw new Error('no link');
    expect(enabled.value.guestLinkUrl.startsWith('https://office.taxoffix.test/meet/')).toBe(true);
    const token = decodeURIComponent(enabled.value.guestLinkUrl.split('/meet/')[1]!);

    const context = await getGuestMeetingContext({ token }, runtime.deps);
    expect(context.isSuccess && context.value.viaMeetingLink).toBe(true);

    const noName = await resolveInvitationToken({ token }, runtime.deps);
    expect(!noName.isSuccess && noName.error.code).toBe('Meeting.Guest.NameRequired');

    const first = await resolveInvitationToken({ token, displayName: 'Luis' }, runtime.deps);
    const second = await resolveInvitationToken({ token, displayName: 'Marta' }, runtime.deps);
    if (!first.isSuccess || !second.isSuccess) throw new Error('resolve failed');
    const luis = await verifyGuestJoinTicket(first.value.shortLivedJoinTicket);
    const marta = await verifyGuestJoinTicket(second.value.shortLivedJoinTicket);
    expect(luis.userId).not.toBe(marta.userId);
    expect(first.value.invitationId).toBeNull();
    expect(first.value.guestUserId).toBe(luis.userId);

    const joined = await joinMeeting(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        user: { userId: luis.userId, displayName: 'Luis', actorType: 'Guest' },
        guestLinkVersion: luis.raw['link_version'] as number,
      },
      joinDeps(runtime),
    );
    expect(joined.isSuccess).toBe(true);
    if (joined.isSuccess) expect(joined.value.requiresAdmission).toBe(true);
  });

  it('a locked meeting, a rotated link or a disabled link stop new people', async () => {
    const runtime = buildMeetingRuntime();
    const { meeting, host } = liveMeeting();
    await runtime.repository.save(meeting);
    const cmd = { tenantId: meeting.tenantId, correlationId: u(), meetingId: meeting.id, actorUserId: host.userId };
    const first = await enableMeetingGuestLink({ ...cmd, rotate: false }, accessDeps(runtime));
    if (!first.isSuccess || !first.value.guestLinkUrl) throw new Error('no link');
    const oldToken = decodeURIComponent(first.value.guestLinkUrl.split('/meet/')[1]!);

    meeting.setLocked({ hostUserId: host.userId, locked: true });
    await runtime.repository.save(meeting);
    const locked = await resolveInvitationToken({ token: oldToken, displayName: 'Luis' }, runtime.deps);
    expect(!locked.isSuccess && locked.error.code).toBe('Meeting.Locked');

    meeting.setLocked({ hostUserId: host.userId, locked: false });
    await runtime.repository.save(meeting);
    await enableMeetingGuestLink({ ...cmd, rotate: true }, accessDeps(runtime));
    const rotated = await resolveInvitationToken({ token: oldToken, displayName: 'Luis' }, runtime.deps);
    expect(!rotated.isSuccess && rotated.error.code).toBe('Meeting.Invitation.NotFound');
  });
});
