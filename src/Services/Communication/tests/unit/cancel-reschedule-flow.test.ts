import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting, type MeetingSnapshot } from '../../src/domain/meetings/meeting.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { cancelMeeting } from '../../src/application/use-cases/cancel-meeting.js';
import { rescheduleMeeting } from '../../src/application/use-cases/reschedule-meeting.js';
import { denyWaitingRoomParticipant } from '../../src/application/use-cases/deny-waiting-room-participant.js';

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
  async findByShortCode(): Promise<null> {
    return null;
  }
  async findByShortCodeAnyTenant(): Promise<null> {
    return null;
  }
  async saveInvitation(inv: MeetingInvitation): Promise<void> {
    this.invitations.set(inv.toSnapshot().id, inv);
  }
  async findInvitationByHash(): Promise<null> {
    return null;
  }
  async findInvitationById(): Promise<null> {
    return null;
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

class FakeEmitter implements RealtimeEmitter {
  readonly emitted: Array<{ event: string; payload: unknown; kind: 'meeting' | 'user' | 'other' }> = [];
  emitToConversation(): void {}
  emitToCall(): void {}
  emitToMeeting(input: { event: string; envelope: { payload: unknown } }): void {
    this.emitted.push({ event: input.event, payload: input.envelope.payload, kind: 'meeting' });
  }
  emitToUser(input: { event: string; envelope: { payload: unknown } }): void {
    this.emitted.push({ event: input.event, payload: input.envelope.payload, kind: 'user' });
  }
  emitToTenant(): void {}
}

function buildHarness() {
  const meetings = new FakeMeetingRepository();
  const publisher = new FakePublisher();
  const emitter = new FakeEmitter();
  return { meetings, publisher, emitter };
}

function scheduledMeeting() {
  const host = { userId: u(), displayName: 'Host' };
  const scheduled = Meeting.schedule({
    tenantId: u(),
    title: 'Consulta',
    host,
    scheduledForUtc: new Date('2026-08-01T10:00:00Z'),
  });
  if (!scheduled.isSuccess) throw new Error();
  return { meeting: scheduled.value, host };
}

describe('cancelMeeting use case', () => {
  it('publishes MeetingCancelled with participant + invited email lists, emits socket dto', async () => {
    const harness = buildHarness();
    const { meeting, host } = scheduledMeeting();
    await harness.meetings.save(meeting);

    // Seed an unused external invitation to verify it lands in invitedEmails.
    const issued = MeetingInvitation.issue({
      meetingId: meeting.id,
      tenantId: meeting.tenantId,
      inviteeKind: 'External',
      inviteeEmail: 'cliente@example.com',
      ttlSeconds: 3600,
      now: new Date(),
    });
    if (!issued.isSuccess) throw new Error();
    await harness.meetings.saveInvitation(issued.value.invitation);

    const result = await cancelMeeting(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        hostUserId: host.userId,
        reason: 'Se pospone hasta septiembre.',
      },
      harness,
    );
    expect(result.isSuccess).toBe(true);
    expect(harness.publisher.events).toHaveLength(1);
    const event = harness.publisher.events[0] as unknown as {
      eventType: string;
      participantUserIds: readonly string[];
      invitedEmails: readonly string[];
      reason: string | null;
    };
    expect(event.eventType).toBe('communication.meeting.cancelled.v1');
    expect(event.participantUserIds).toContain(host.userId);
    expect(event.invitedEmails).toContain('cliente@example.com');
    expect(event.reason).toBe('Se pospone hasta septiembre.');
    expect(harness.emitter.emitted.some((e) => e.event === 'meeting.cancelled')).toBe(true);
  });

  it('rejects if actor is not host', async () => {
    const harness = buildHarness();
    const { meeting } = scheduledMeeting();
    await harness.meetings.save(meeting);
    const result = await cancelMeeting(
      { tenantId: meeting.tenantId, correlationId: u(), meetingId: meeting.id, hostUserId: u() },
      harness,
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.HostOnly');
  });
});

describe('rescheduleMeeting use case', () => {
  it('publishes MeetingRescheduled with previous + new dates and emits socket dto', async () => {
    const harness = buildHarness();
    const { meeting, host } = scheduledMeeting();
    await harness.meetings.save(meeting);
    const newIso = '2026-08-05T15:00:00.000Z';

    const result = await rescheduleMeeting(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        hostUserId: host.userId,
        newScheduledForUtc: newIso,
      },
      harness,
    );
    expect(result.isSuccess).toBe(true);
    const event = harness.publisher.events[0] as unknown as {
      eventType: string;
      previousScheduledForUtc: string | null;
      newScheduledForUtc: string | null;
    };
    expect(event.eventType).toBe('communication.meeting.rescheduled.v1');
    expect(event.previousScheduledForUtc).toBe('2026-08-01T10:00:00.000Z');
    expect(event.newScheduledForUtc).toBe(newIso);
    expect(harness.emitter.emitted.some((e) => e.event === 'meeting.rescheduled')).toBe(true);
  });

  it('rejects invalid ISO date at use-case layer, not just at Zod', async () => {
    const harness = buildHarness();
    const { meeting, host } = scheduledMeeting();
    await harness.meetings.save(meeting);
    const result = await rescheduleMeeting(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        hostUserId: host.userId,
        newScheduledForUtc: 'not-a-date',
      },
      harness,
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.BadPayload');
  });
});

describe('denyWaitingRoomParticipant use case', () => {
  it('emits ParticipantDenied to both the user (their spinner) and the meeting room', async () => {
    const harness = buildHarness();
    const { meeting, host } = scheduledMeeting();
    meeting.start({ hostUserId: host.userId });
    const attendee = u();
    meeting.requestJoin({ userId: attendee, displayName: 'X', hasValidInvitation: false, passcodeMatch: null });
    await harness.meetings.save(meeting);

    const result = await denyWaitingRoomParticipant(
      {
        tenantId: meeting.tenantId,
        correlationId: u(),
        meetingId: meeting.id,
        hostUserId: host.userId,
        targetUserId: attendee,
      },
      harness,
    );
    expect(result.isSuccess).toBe(true);
    expect(harness.publisher.events[0]?.eventType).toBe('communication.meeting.participant_denied.v1');

    const denied = harness.emitter.emitted.filter((e) => e.event === 'meeting.participant.denied');
    expect(denied.some((e) => e.kind === 'user')).toBe(true);
    expect(denied.some((e) => e.kind === 'meeting')).toBe(true);
  });
});
