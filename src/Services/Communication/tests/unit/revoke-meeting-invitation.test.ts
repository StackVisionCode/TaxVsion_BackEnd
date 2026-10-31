import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import { revokeMeetingInvitation } from '../../src/application/use-cases/revoke-meeting-invitation.js';
import { finalizeEndedMeeting } from '../../src/application/use-cases/finalize-ended-meeting.js';
import { buildMeetingRuntime } from '../helpers/fake-meeting-runtime.js';

const u = (): string => randomUUID();

async function liveWithGuest(runtime: ReturnType<typeof buildMeetingRuntime>, opts: { guestInside: boolean }) {
  const host = { userId: u(), displayName: 'Ana' };
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Consulta', host, requireWaitingRoom: false });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  const issued = MeetingInvitation.issue({ meetingId: meeting.id, tenantId: meeting.tenantId, inviteeKind: 'External', inviteeEmail: 'g@x.com', ttlSeconds: 3600, now: new Date() });
  if (!issued.isSuccess) throw new Error('issue failed');
  const invitation = issued.value.invitation;
  if (opts.guestInside) {
    invitation.consumeForGuestJoin(new Date());
    meeting.requestJoin({ userId: invitation.guestUserId, displayName: 'Pepe', actorType: 'Guest', hasValidInvitation: true, passcodeMatch: null });
  }
  await runtime.repository.save(meeting);
  await runtime.repository.saveInvitation(invitation);
  return { meeting, host, invitation };
}

describe('revokeMeetingInvitation (F6, INV-09 H-5)', () => {
  it('con el guest dentro: salida forzada Revoked, socket cerrado, evento y log con ejected', async () => {
    const runtime = buildMeetingRuntime();
    const { meeting, host, invitation } = await liveWithGuest(runtime, { guestInside: true });
    const disconnected: string[] = [];
    const result = await revokeMeetingInvitation(
      { tenantId: meeting.tenantId, meetingId: meeting.id, invitationId: invitation.id, actorUserId: host.userId, correlationId: 'c1' },
      { ...runtime.deps, ejection: { ...runtime.deps, sessionTerminator: { disconnectUser: async (_t, userId) => void disconnected.push(userId) } } },
    );
    expect(result).toMatchObject({ isSuccess: true, value: { ejected: true } });
    expect(disconnected).toEqual([invitation.guestUserId]);
    const after = runtime.repository.meetings.get(meeting.id)?.toSnapshot();
    expect(after?.participants.find((p) => p.userId === invitation.guestUserId)?.status).toBe('Left');
    expect(runtime.publisher.events.map((e) => e.eventType)).toEqual(
      expect.arrayContaining(['communication.meeting.participant_forced_leave.v1', 'communication.meeting.invitation_revoked.v1']),
    );
    expect(runtime.meetingEvents.entries.find((e) => e.type === 'invitation.revoked')?.payload).toMatchObject({ ejected: true });
    // Idempotente: revocar otra vez no vuelve a publicar.
    const again = await revokeMeetingInvitation(
      { tenantId: meeting.tenantId, meetingId: meeting.id, invitationId: invitation.id, actorUserId: host.userId },
      runtime.deps,
    );
    expect(again).toMatchObject({ isSuccess: true, value: { ejected: false } });
    expect(runtime.publisher.events.filter((e) => e.eventType === 'communication.meeting.invitation_revoked.v1')).toHaveLength(1);
  });

  it('sin el guest dentro: solo revoca; un cohost puede, un asistente no', async () => {
    const runtime = buildMeetingRuntime();
    const { meeting, host, invitation } = await liveWithGuest(runtime, { guestInside: false });
    const denied = await revokeMeetingInvitation(
      { tenantId: meeting.tenantId, meetingId: meeting.id, invitationId: invitation.id, actorUserId: u() },
      runtime.deps,
    );
    expect(denied).toMatchObject({ isSuccess: false, error: { code: 'Meeting.HostOnly' } });
    const ok = await revokeMeetingInvitation(
      { tenantId: meeting.tenantId, meetingId: meeting.id, invitationId: invitation.id, actorUserId: host.userId },
      { ...runtime.deps, ejection: runtime.deps },
    );
    expect(ok).toMatchObject({ isSuccess: true, value: { ejected: false } });
    expect(invitation.toSnapshot().revokedAtUtc).not.toBeNull();
  });
});

describe('finalizeEndedMeeting cierra las invitaciones abiertas (F6, D-48)', () => {
  it('las abiertas pasan a closedAtUtc con invitation.expired y el evento; las revocadas no se tocan', async () => {
    const runtime = buildMeetingRuntime();
    const { meeting, host, invitation } = await liveWithGuest(runtime, { guestInside: false });
    const revoked = MeetingInvitation.issue({ meetingId: meeting.id, tenantId: meeting.tenantId, inviteeKind: 'Employee', inviteeUserId: u(), ttlSeconds: 3600, now: new Date() });
    if (!revoked.isSuccess) throw new Error('issue failed');
    revoked.value.invitation.revoke(new Date());
    await runtime.repository.saveInvitation(revoked.value.invitation);

    meeting.end({ byUserId: host.userId });
    await runtime.repository.save(meeting);
    await finalizeEndedMeeting({ tenantId: meeting.tenantId, correlationId: 'c2', snapshot: meeting.toSnapshot(), actorUserId: host.userId }, runtime.deps);

    expect(invitation.toSnapshot().closedAtUtc).not.toBeNull();
    expect(revoked.value.invitation.toSnapshot().closedAtUtc).toBeNull();
    const expired = runtime.publisher.events.filter((e) => e.eventType === 'communication.meeting.invitation_expired.v1');
    expect(expired).toHaveLength(1);
    expect(expired[0]).toMatchObject({ invitationId: invitation.id, reason: 'Ended' });
    expect(runtime.meetingEvents.entries.filter((e) => e.type === 'invitation.expired')).toHaveLength(1);
  });
});
