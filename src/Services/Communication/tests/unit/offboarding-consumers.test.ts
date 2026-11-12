import { describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting, type MeetingSnapshot } from '../../src/domain/meetings/meeting.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type {
  UserDirectoryRepository,
  UserDirectoryEntrySnapshot,
} from '../../src/application/ports/user-directory-repository.js';
import type { UserPermissionsProjectionRepository } from '../../src/application/ports/user-permissions-projection-repository.js';
import type { CustomerPortalAccountRepository } from '../../src/application/ports/customer-portal-account-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import { bindOffboardingConsumers } from '../../src/application/event-handlers/offboarding-consumers.js';

function u(): string {
  return randomUUID();
}

class FakeMeetingRepository implements MeetingRepository {
  async listScheduledForReminder(): Promise<Meeting[]> {
    return [];
  }
  private readonly meetings = new Map<string, Meeting>();
  async save(meeting: Meeting): Promise<void> {
    this.meetings.set(meeting.id, meeting);
  }
  get(meetingId: string): Meeting | undefined {
    return this.meetings.get(meetingId);
  }
  async listActiveHostedBy(tenantId: string, hostUserId: string): Promise<Meeting[]> {
    return [...this.meetings.values()].filter(
      (m) =>
        m.tenantId === tenantId &&
        m.hostUserId === hostUserId &&
        (m.status === 'Scheduled' || m.status === 'Live'),
    );
  }
  async countActiveHostedBy(tenantId: string, hostUserId: string): Promise<number> {
    return (await this.listActiveHostedBy(tenantId, hostUserId)).length;
  }
  async listScheduledWithParticipant(tenantId: string, userId: string): Promise<Meeting[]> {
    return [...this.meetings.values()].filter(
      (m) =>
        m.tenantId === tenantId &&
        m.status === 'Scheduled' &&
        m.hostUserId !== userId &&
        m.toSnapshot().participants.some((p) => p.userId === userId && p.status !== 'Removed'),
    );
  }
  async countScheduledWithParticipant(tenantId: string, userId: string): Promise<number> {
    return (await this.listScheduledWithParticipant(tenantId, userId)).length;
  }
  readonly invitations = new Map<string, MeetingInvitation>();
  async listOpenInvitationsForInvitee(tenantId: string, inviteeUserId: string): Promise<MeetingInvitation[]> {
    return [...this.invitations.values()].filter((i) => {
      const s = i.toSnapshot();
      return (
        s.tenantId === tenantId &&
        s.inviteeUserId === inviteeUserId &&
        s.revokedAtUtc === null &&
        s.closedAtUtc === null
      );
    });
  }
  async findById(): Promise<Meeting | null> {
    return null;
  }
  async findByShortCode(): Promise<null> {
    return null;
  }
  async findByAppointmentId(): Promise<null> {
    return null;
  }
  async findByShortCodeAnyTenant(): Promise<null> {
    return null;
  }
  async findByIdAnyTenant(): Promise<null> {
    return null;
  }
  async saveInvitation(invitation: MeetingInvitation): Promise<void> {
    this.invitations.set(invitation.id, invitation);
  }
  async findInvitationByHash(): Promise<null> {
    return null;
  }
  async findInvitationById(): Promise<null> {
    return null;
  }
  async listInvitationsByMeeting(_tenantId: string, meetingId: string): Promise<MeetingInvitation[]> {
    return [...this.invitations.values()].filter((i) => i.toSnapshot().meetingId === meetingId);
  }
  async listLive(): Promise<Meeting[]> {
    return [];
  }
  async listScheduledBefore(): Promise<Meeting[]> {
    return [];
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
  async getStatsForUser(): Promise<{
    today: number;
    thisWeek: number;
    liveNow: number;
    transcriptsAvailable: number;
  }> {
    return { today: 0, thisWeek: 0, liveNow: 0, transcriptsAvailable: 0 };
  }
}

class FakePublisher implements IntegrationEventPublisher {
  readonly events: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.events.push(event);
  }
}

class FakeEmitter implements RealtimeEmitter {
  readonly emitted: Array<{ event: string; kind: 'meeting' | 'user' }> = [];
  emitToConversation(): void {}
  emitToCall(): void {}
  emitToMeeting(input: { event: string }): void {
    this.emitted.push({ event: input.event, kind: 'meeting' });
  }
  emitToUser(input: { event: string }): void {
    this.emitted.push({ event: input.event, kind: 'user' });
  }
  emitToTenant(): void {}
  emitToTenantStaff(): void {}
  emitToTenantMembers(): void {}
  emitBroadcast(): void {}
}

const TENANT = u();

function setup(successor: UserDirectoryEntrySnapshot | null) {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const register = (t: string, h: (env: IncomingEnvelope) => Promise<void>) => {
    handlers.set(t, h);
  };
  const userPermissions: UserPermissionsProjectionRepository = {
    upsert: vi.fn(),
    upsertIdentityPreservingPermissions: vi.fn(),
    findByUserId: vi.fn(),
    findActiveByTenantAndPermission: vi.fn(),
    findActiveSupportRecipients: vi.fn(),
    markInactive: vi.fn(),
    markActive: vi.fn(),
    findActiveByTenantAndRoleId: vi.fn(),
  };
  const userDirectory: UserDirectoryRepository = {
    upsert: vi.fn(),
    findByUserId: vi.fn(async (id: string) => (successor && id === successor.userId ? successor : null)),
    markInactive: vi.fn(),
    markActive: vi.fn(),
    searchByDisplayNameOrEmail: vi.fn(),
  };
  const customerPortalAccounts: CustomerPortalAccountRepository = {
    upsert: vi.fn(),
    markInactiveByUserId: vi.fn(),
    markActiveByUserId: vi.fn(),
    findActiveByCustomerId: vi.fn(),
    findActiveByCustomerIds: vi.fn(),
    findActiveByUserId: vi.fn(),
  };
  const meetings = new FakeMeetingRepository();
  const publisher = new FakePublisher();
  const emitter = new FakeEmitter();
  bindOffboardingConsumers(register, {
    userPermissions,
    userDirectory,
    customerPortalAccounts,
    meetings,
    publisher,
    emitter,
  });
  return { handlers, userPermissions, userDirectory, customerPortalAccounts, meetings, publisher, emitter };
}

function eligibleSuccessor(userId: string): UserDirectoryEntrySnapshot {
  return {
    userId,
    tenantId: TENANT,
    displayName: 'Successor',
    email: 'successor@example.com',
    isActive: true,
    actorType: 'TenantEmployee',
    avatarFileId: null,
    updatedAtUtc: new Date(),
  };
}

function scheduledMeeting(hostUserId: string): Meeting {
  const result = Meeting.schedule({
    tenantId: TENANT,
    title: 'Consulta',
    host: { userId: hostUserId, displayName: 'Leaver' },
    scheduledForUtc: new Date('2026-08-01T10:00:00Z'),
  });
  if (!result.isSuccess) throw new Error('schedule failed');
  return result.value;
}

function liveMeeting(hostUserId: string): Meeting {
  const meeting = scheduledMeeting(hostUserId);
  meeting.start({ hostUserId });
  return meeting;
}

function offboardEnvelope(payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: u(),
    eventType: 'auth.user.offboarded.v1',
    tenantId: TENANT,
    correlationId: u(),
    occurredOnUtc: new Date().toISOString(),
    payload,
  };
}

describe('offboarding consumer — auth.user.offboarded.v1', () => {
  it('always inactivates the three projections (offboard includes deactivation)', async () => {
    const leaver = u();
    const ctx = setup(null);

    await ctx.handlers.get('auth.user.offboarded.v1')!(offboardEnvelope({ userId: leaver }));

    expect(ctx.userPermissions.markInactive).toHaveBeenCalledWith(leaver, expect.any(Date));
    expect(ctx.userDirectory.markInactive).toHaveBeenCalledWith(leaver);
    expect(ctx.customerPortalAccounts.markInactiveByUserId).toHaveBeenCalledWith(leaver);
  });

  it('offboarding impact counts active (scheduled or live) meetings hosted by the leaver', async () => {
    const leaver = u();
    const ctx = setup(null);
    await ctx.meetings.save(scheduledMeeting(leaver));
    await ctx.meetings.save(liveMeeting(leaver));
    await ctx.meetings.save(scheduledMeeting(u())); // de otro host, no cuenta

    const count = await ctx.meetings.countActiveHostedBy(TENANT, leaver);

    expect(count).toBe(2);
  });

  it('reassigns the leaver hosted meetings to an eligible successor', async () => {
    const leaver = u();
    const successor = u();
    const ctx = setup(eligibleSuccessor(successor));
    const meeting = scheduledMeeting(leaver);
    await ctx.meetings.save(meeting);

    await ctx.handlers.get('auth.user.offboarded.v1')!(
      offboardEnvelope({ userId: leaver, successorUserId: successor, offboardedByUserId: u() }),
    );

    expect(ctx.meetings.get(meeting.id)?.hostUserId).toBe(successor);
    expect(
      ctx.publisher.events.some((e) => e.eventType === 'communication.meeting.host_transferred.v1'),
    ).toBe(true);
  });

  it('cancels a scheduled meeting when there is no successor', async () => {
    const leaver = u();
    const ctx = setup(null);
    const meeting = scheduledMeeting(leaver);
    await ctx.meetings.save(meeting);

    await ctx.handlers.get('auth.user.offboarded.v1')!(offboardEnvelope({ userId: leaver }));

    expect(ctx.meetings.get(meeting.id)?.status).toBe('Cancelled');
    expect(ctx.publisher.events.some((e) => e.eventType === 'communication.meeting.cancelled.v1')).toBe(true);
    expect(ctx.emitter.emitted.some((e) => e.event === 'meeting.cancelled')).toBe(true);
  });

  it('ends a live meeting when there is no successor', async () => {
    const leaver = u();
    const ctx = setup(null);
    const meeting = liveMeeting(leaver);
    await ctx.meetings.save(meeting);

    await ctx.handlers.get('auth.user.offboarded.v1')!(offboardEnvelope({ userId: leaver }));

    expect(ctx.meetings.get(meeting.id)?.status).toBe('Ended');
    expect(ctx.publisher.events.some((e) => e.eventType === 'communication.meeting.ended.v1')).toBe(true);
  });

  it('falls back to cancel when the named successor is not eligible', async () => {
    const leaver = u();
    const successor = u();
    // Successor exists but is inactive -> not eligible.
    const ctx = setup({ ...eligibleSuccessor(successor), isActive: false });
    const meeting = scheduledMeeting(leaver);
    await ctx.meetings.save(meeting);

    await ctx.handlers.get('auth.user.offboarded.v1')!(
      offboardEnvelope({ userId: leaver, successorUserId: successor }),
    );

    expect(ctx.meetings.get(meeting.id)?.status).toBe('Cancelled');
  });

  it('#4: leaves scheduled meetings of other hosts and its open invitations are revoked', async () => {
    const leaver = u();
    const ctx = setup(null);
    const other = scheduledMeeting(u());
    // Queda como participante (p.ej. host viejo degradado a Attendee) de una reunión de otro.
    other.reassignHostBySystem({ newHostUserId: leaver, newHostDisplayName: 'Leaver' });
    other.reassignHostBySystem({ newHostUserId: u(), newHostDisplayName: 'Other host' });
    await ctx.meetings.save(other);
    const issued = MeetingInvitation.issue({
      meetingId: other.id,
      tenantId: TENANT,
      inviteeKind: 'Employee',
      inviteeUserId: leaver,
      ttlSeconds: 3600,
      now: new Date(),
    });
    if (!issued.isSuccess) throw new Error('issue failed');
    await ctx.meetings.saveInvitation(issued.value.invitation);

    await ctx.handlers.get('auth.user.offboarded.v1')!(offboardEnvelope({ userId: leaver }));

    const saved = ctx.meetings.get(other.id)!;
    expect(saved.status).toBe('Scheduled');
    expect(saved.notifiableUserIds()).not.toContain(leaver);
    expect(saved.toSnapshot().participants.find((p) => p.userId === leaver)?.status).toBe('Removed');
    expect(
      ctx.meetings.invitations.get(issued.value.invitation.id)?.toSnapshot().revokedAtUtc,
    ).not.toBeNull();
  });

  it('#4: the reassigned meeting no longer notifies the old host', async () => {
    const leaver = u();
    const successor = u();
    const ctx = setup(eligibleSuccessor(successor));
    const meeting = scheduledMeeting(leaver);
    await ctx.meetings.save(meeting);

    await ctx.handlers.get('auth.user.offboarded.v1')!(
      offboardEnvelope({ userId: leaver, successorUserId: successor }),
    );

    const saved = ctx.meetings.get(meeting.id)!;
    expect(saved.hostUserId).toBe(successor);
    expect(saved.notifiableUserIds()).toEqual([successor]);
  });

  it('#4: the cancellation notice of its own meetings does not go to the leaver', async () => {
    const leaver = u();
    const ctx = setup(null);
    await ctx.meetings.save(scheduledMeeting(leaver));

    await ctx.handlers.get('auth.user.offboarded.v1')!(offboardEnvelope({ userId: leaver }));

    const cancelled = ctx.publisher.events.find(
      (e) => e.eventType === 'communication.meeting.cancelled.v1',
    ) as unknown as {
      participantUserIds: string[];
    };
    expect(cancelled.participantUserIds).not.toContain(leaver);
  });

  it('acknowledges the step to Auth last, with the registry service and step names', async () => {
    const leaver = u();
    const ctx = setup(null);
    await ctx.meetings.save(scheduledMeeting(leaver));
    const envelope = offboardEnvelope({ userId: leaver });

    await ctx.handlers.get('auth.user.offboarded.v1')?.(envelope);

    const last = ctx.publisher.events.at(-1) as unknown as Record<string, unknown>;
    expect(last).toMatchObject({
      eventType: 'auth.user.offboarding_step_completed.v1',
      tenantId: TENANT,
      correlationId: envelope.correlationId,
      userId: leaver,
      service: 'Communication',
      step: 'offboarding-consumers',
    });
    expect(typeof last['completedAtUtc']).toBe('string');
  });

  it('does not acknowledge when a step fails (the retry acknowledges later)', async () => {
    const leaver = u();
    const ctx = setup(null);
    ctx.meetings.listActiveHostedBy = async () => {
      throw new Error('db down');
    };

    await expect(
      ctx.handlers.get('auth.user.offboarded.v1')?.(offboardEnvelope({ userId: leaver })),
    ).rejects.toThrow('db down');

    expect(ctx.publisher.events.some((e) => e.eventType === 'auth.user.offboarding_step_completed.v1')).toBe(
      false,
    );
  });
});
