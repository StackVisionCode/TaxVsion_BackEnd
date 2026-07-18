import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import type { MeetingSnapshot } from '../../src/domain/meetings/meeting.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { PasscodeHasher } from '../../src/application/ports/passcode-hasher.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { TurnCredentialFactory } from '../../src/application/ports/turn-credential-factory.js';
import { createMeetingInvitations } from '../../src/application/use-cases/create-meeting-invitations.js';
import { resolveInvitationToken } from '../../src/application/use-cases/resolve-invitation-token.js';
import { revokeMeetingInvitation } from '../../src/application/use-cases/revoke-meeting-invitation.js';
import { listMeetingInvitations } from '../../src/application/use-cases/list-meeting-invitations.js';
import { joinMeeting } from '../../src/application/use-cases/join-meeting.js';

function u(): string {
  return randomUUID();
}

class FakeMeetingRepository implements MeetingRepository {
  private readonly meetings = new Map<string, Meeting>();
  private readonly invitations = new Map<string, MeetingInvitation>();

  async save(meeting: Meeting): Promise<void> {
    this.meetings.set(meeting.id, meeting);
  }
  async findById(tenantId: string, meetingId: string): Promise<Meeting | null> {
    const m = this.meetings.get(meetingId);
    return m && m.tenantId === tenantId ? m : null;
  }
  async findByShortCode(): Promise<Meeting | null> {
    return null;
  }
  async findByShortCodeAnyTenant(): Promise<Meeting | null> {
    return null;
  }
  async saveInvitation(invitation: MeetingInvitation): Promise<void> {
    this.invitations.set(invitation.toSnapshot().id, invitation);
  }
  async findInvitationByHash(tokenHash: string): Promise<MeetingInvitation | null> {
    for (const inv of this.invitations.values()) {
      if (inv.toSnapshot().tokenHash === tokenHash) return inv;
    }
    return null;
  }
  async findInvitationById(tenantId: string, invitationId: string): Promise<MeetingInvitation | null> {
    const inv = this.invitations.get(invitationId);
    return inv && inv.toSnapshot().tenantId === tenantId ? inv : null;
  }
  async listInvitationsByMeeting(tenantId: string, meetingId: string): Promise<MeetingInvitation[]> {
    return [...this.invitations.values()].filter((inv) => {
      const s = inv.toSnapshot();
      return s.tenantId === tenantId && s.meetingId === meetingId;
    });
  }
  async listUpcomingForUser(): Promise<MeetingSnapshot[]> {
    return [];
  }
  async countUpcomingForUser(): Promise<number> {
    return 0;
  }
  async listPastForUser(): Promise<MeetingSnapshot[]> {
    return [];
  }
  async countPastForUser(): Promise<number> {
    return 0;
  }
}

class FakePublisher implements IntegrationEventPublisher {
  readonly events: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.events.push(event);
  }
}

class FakePasscodeHasher implements PasscodeHasher {
  async hash(plain: string): Promise<string> {
    return `hashed:${plain}`;
  }
  async verify(hash: string, plain: string): Promise<boolean> {
    return hash === `hashed:${plain}`;
  }
}

class FakeConversationRepository implements ConversationRepository {
  async save(): Promise<void> {}
  async findById(): Promise<null> {
    return null;
  }
  async findByUniquenessKey(): Promise<null> {
    return null;
  }
  async listForUser(): Promise<[]> {
    return [];
  }
  async countForUser(): Promise<number> {
    return 0;
  }
  async listMessages(): Promise<[]> {
    return [];
  }
  async countUnreadForUser(): Promise<number> {
    return 0;
  }
}

class FakeTurnCredentialFactory implements TurnCredentialFactory {
  issue(): { iceServers: readonly []; expiresAtUtc: string } {
    return { iceServers: [], expiresAtUtc: new Date(0).toISOString() };
  }
}

function buildHarness() {
  const meetings = new FakeMeetingRepository();
  const publisher = new FakePublisher();
  const passcodes = new FakePasscodeHasher();
  const conversations = new FakeConversationRepository();
  const turn = new FakeTurnCredentialFactory();
  return { meetings, publisher, passcodes, conversations, turn };
}

function liveMeeting() {
  const host = { userId: u(), displayName: 'Host' };
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Consulta', host });
  if (!scheduled.isSuccess) throw new Error();
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  return { meeting, host };
}

describe('createMeetingInvitations', () => {
  it('host creates an invitation for an external invitee and publishes the event', async () => {
    const harness = buildHarness();
    const { meeting, host } = liveMeeting();
    await harness.meetings.save(meeting);

    const result = await createMeetingInvitations(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        actorUserId: host.userId,
        invitees: [{ kind: 'External', email: 'cliente@example.com', name: 'Cliente Externo' }],
      },
      harness,
    );
    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value.invitations).toHaveLength(1);
    expect(result.value.invitations[0]?.joinUrl).toContain('/join?token=');
    expect(harness.publisher.events).toHaveLength(1);
    expect(harness.publisher.events[0]?.eventType).toBe('communication.meeting.invitation_created.v1');
  });

  it('rejects when actor is not host/cohost', async () => {
    const harness = buildHarness();
    const { meeting } = liveMeeting();
    await harness.meetings.save(meeting);

    const result = await createMeetingInvitations(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        actorUserId: u(),
        invitees: [{ kind: 'External', email: 'cliente@example.com' }],
      },
      harness,
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.HostOnly');
  });
});

describe('resolveInvitationToken + guest join-meeting flow', () => {
  async function issueInvitation(harness: ReturnType<typeof buildHarness>, meeting: Meeting, hostUserId: string) {
    const created = await createMeetingInvitations(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        actorUserId: hostUserId,
        invitees: [{ kind: 'External', email: 'guest@example.com', name: 'Invitado Externo' }],
      },
      harness,
    );
    if (!created.isSuccess) throw new Error();
    const joinUrl = created.value.invitations[0]!.joinUrl;
    const token = new URL(joinUrl).searchParams.get('token')!;
    return token;
  }

  it('resolves a valid token to a short-lived join ticket and lets the guest join the waiting room', async () => {
    const harness = buildHarness();
    const { meeting, host } = liveMeeting();
    await harness.meetings.save(meeting);
    const token = await issueInvitation(harness, meeting, host.userId);

    const resolved = await resolveInvitationToken({ token }, harness);
    expect(resolved.isSuccess).toBe(true);
    if (!resolved.isSuccess) return;
    expect(resolved.value.meetingId).toBe(meeting.id);
    expect(resolved.value.tenantId).toBe(meeting.tenantId);
    expect(typeof resolved.value.shortLivedJoinTicket).toBe('string');

    // Resolviendo NO consume la invitation todavia (solo el join real la consume).
    const invitations = await listMeetingInvitations(
      { tenantId: meeting.tenantId, meetingId: meeting.id, actorUserId: host.userId },
      harness,
    );
    expect(invitations.isSuccess).toBe(true);
    if (invitations.isSuccess) expect(invitations.value.invitations[0]?.usedAt).toBeNull();

    const guestUserId = `guest:${resolved.value.invitationId}`;
    const joinResult = await joinMeeting(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        user: { userId: guestUserId, displayName: 'Invitado Externo', actorType: 'Guest' },
        guestInvitationId: resolved.value.invitationId,
      },
      harness,
    );
    expect(joinResult.isSuccess).toBe(true);
    if (!joinResult.isSuccess) return;
    expect(joinResult.value.requiresAdmission).toBe(true);
    const guestParticipant = joinResult.value.snapshot.participants.find((p) => p.userId === guestUserId);
    expect(guestParticipant?.status).toBe('Waiting');
    expect(guestParticipant?.role).toBe('Attendee');

    // La invitation queda consumida tras el join real.
    const afterJoin = await listMeetingInvitations(
      { tenantId: meeting.tenantId, meetingId: meeting.id, actorUserId: host.userId },
      harness,
    );
    if (afterJoin.isSuccess) expect(afterJoin.value.invitations[0]?.usedAt).not.toBeNull();
  });

  it('a second join attempt with the same invitationId fails (already used)', async () => {
    const harness = buildHarness();
    const { meeting, host } = liveMeeting();
    await harness.meetings.save(meeting);
    const token = await issueInvitation(harness, meeting, host.userId);
    const resolved = await resolveInvitationToken({ token }, harness);
    if (!resolved.isSuccess) throw new Error();

    const guestUserId = `guest:${resolved.value.invitationId}`;
    const first = await joinMeeting(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        user: { userId: guestUserId, displayName: 'Invitado', actorType: 'Guest' },
        guestInvitationId: resolved.value.invitationId,
      },
      harness,
    );
    expect(first.isSuccess).toBe(true);

    const second = await joinMeeting(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        user: { userId: guestUserId, displayName: 'Invitado', actorType: 'Guest' },
        guestInvitationId: resolved.value.invitationId,
      },
      harness,
    );
    expect(second.isSuccess).toBe(false);
    if (!second.isSuccess) expect(second.error.code).toBe('Meeting.Invitation.AlreadyUsed');
  });

  it('a revoked invitation is rejected with the same generic error as not-found (anti-enumeration)', async () => {
    const harness = buildHarness();
    const { meeting, host } = liveMeeting();
    await harness.meetings.save(meeting);
    const token = await issueInvitation(harness, meeting, host.userId);

    const listResult = await listMeetingInvitations(
      { tenantId: meeting.tenantId, meetingId: meeting.id, actorUserId: host.userId },
      harness,
    );
    if (!listResult.isSuccess) throw new Error();
    const invitationId = listResult.value.invitations[0]!.id;

    const revokeResult = await revokeMeetingInvitation(
      { tenantId: meeting.tenantId, meetingId: meeting.id, invitationId, actorUserId: host.userId },
      harness,
    );
    expect(revokeResult.isSuccess).toBe(true);

    const resolved = await resolveInvitationToken({ token }, harness);
    expect(resolved.isSuccess).toBe(false);
    if (!resolved.isSuccess) expect(resolved.error.code).toBe('Meeting.Invitation.NotFound');
  });

  it('an unknown token resolves to the same generic not-found error', async () => {
    const harness = buildHarness();
    const resolved = await resolveInvitationToken({ token: 'a'.repeat(64) }, harness);
    expect(resolved.isSuccess).toBe(false);
    if (!resolved.isSuccess) expect(resolved.error.code).toBe('Meeting.Invitation.NotFound');
  });
});
