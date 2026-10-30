import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import { getGuestMeetingContext } from '../../src/application/use-cases/get-guest-meeting-context.js';
import { resolveInvitationToken } from '../../src/application/use-cases/resolve-invitation-token.js';
import { joinMeeting } from '../../src/application/use-cases/join-meeting.js';
import { buildMeetingRuntime } from '../helpers/fake-meeting-runtime.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';
import type { PasscodeHasher } from '../../src/application/ports/passcode-hasher.js';
import type { TurnCredentialFactory } from '../../src/application/ports/turn-credential-factory.js';

const u = (): string => randomUUID();

function scheduledMeeting(tenantId: string, opts: { waitingRoom?: boolean } = {}) {
  const host = { userId: u(), displayName: 'Ana Host' };
  const scheduled = Meeting.schedule({
    tenantId,
    title: 'Declaracion 2025',
    host,
    ...(opts.waitingRoom === undefined ? {} : { requireWaitingRoom: opts.waitingRoom }),
  });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  return { meeting: scheduled.value, host };
}

async function inviteExternal(runtime: ReturnType<typeof buildMeetingRuntime>, meeting: Meeting) {
  const issued = MeetingInvitation.issue({
    meetingId: meeting.id,
    tenantId: meeting.tenantId,
    inviteeKind: 'External',
    inviteeEmail: 'guest@example.com',
    inviteeName: 'Pepe',
    ttlSeconds: 3600,
    now: new Date(),
  });
  if (!issued.isSuccess) throw new Error('issue failed');
  await runtime.repository.saveInvitation(issued.value.invitation);
  return issued.value;
}

describe('getGuestMeetingContext (F6, T-63)', () => {
  it('devuelve titulo, host, estado, politica de guests y la politica de media; 404 uniforme si el token no vale', async () => {
    const runtime = buildMeetingRuntime({ guestsWaitingRoomDefault: true });
    const tenantId = u();
    const { meeting } = scheduledMeeting(tenantId, { waitingRoom: false });
    await runtime.repository.save(meeting);
    const { plainToken } = await inviteExternal(runtime, meeting);

    const ok = await getGuestMeetingContext({ token: plainToken }, runtime.deps);
    expect(ok.isSuccess).toBe(true);
    if (ok.isSuccess) {
      expect(ok.value).toMatchObject({
        meetingTitle: 'Declaracion 2025',
        hostDisplayName: 'Ana Host',
        status: 'Scheduled',
        inviteeName: 'Pepe',
        guestsAllowed: true,
        waitingRoom: true,
      });
      expect(ok.value.mediaPolicy.reactions.enabled).toBe(true);
    }

    const bad = await getGuestMeetingContext({ token: 'f'.repeat(64) }, runtime.deps);
    expect(bad).toMatchObject({ isSuccess: false, error: { code: 'Meeting.Invitation.NotFound' } });
  });

  it('con la reunion terminada el token autentico informa Ended; con guests apagados lo dice sin ocultar la reunion', async () => {
    const runtime = buildMeetingRuntime({ guestsAllowed: false });
    const tenantId = u();
    const { meeting, host } = scheduledMeeting(tenantId);
    await runtime.repository.save(meeting);
    const { invitation, plainToken } = await inviteExternal(runtime, meeting);

    const off = await getGuestMeetingContext({ token: plainToken }, runtime.deps);
    expect(off.isSuccess && off.value.guestsAllowed).toBe(false);
    const ticket = await resolveInvitationToken({ token: plainToken }, runtime.deps);
    expect(ticket).toMatchObject({ isSuccess: false, error: { code: 'Meeting.GuestsNotAllowed' } });

    meeting.cancel({ hostUserId: host.userId });
    invitation.close(new Date());
    await runtime.repository.save(meeting);
    await runtime.repository.saveInvitation(invitation);
    const ended = await getGuestMeetingContext({ token: plainToken }, runtime.deps);
    expect(ended.isSuccess && ended.value.status).toBe('Ended');
  });

  it('la politica de la oficina manda al guest a la sala de espera aunque la reunion no la exija', async () => {
    const runtime = buildMeetingRuntime({ guestsWaitingRoomDefault: true });
    const tenantId = u();
    const { meeting, host } = scheduledMeeting(tenantId, { waitingRoom: false });
    meeting.start({ hostUserId: host.userId });
    await runtime.repository.save(meeting);
    const { invitation } = await inviteExternal(runtime, meeting);

    const joined = await joinMeeting(
      {
        tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        user: { userId: invitation.guestUserId, displayName: 'Pepe', actorType: 'Guest' },
        guestInvitationId: invitation.id,
      },
      {
        ...runtime.deps,
        passcodes: { verify: async () => true, hash: async () => 'h' } as unknown as PasscodeHasher,
        turn: { issue: () => ({ iceServers: [], expiresAtUtc: new Date().toISOString() }) } as unknown as TurnCredentialFactory,
        effectiveSettings: new FakeEffectiveSettingsResolver({ guestsWaitingRoomDefault: true }),
      },
    );
    expect(joined.isSuccess).toBe(true);
    if (joined.isSuccess) expect(joined.value.requiresAdmission).toBe(true);
    const log = runtime.meetingEvents.entries.map((e) => e.type);
    expect(log).toContain('invitation.used');
    expect(runtime.publisher.events.map((e) => e.eventType)).toContain('communication.meeting.invitation_used.v1');
  });
});
