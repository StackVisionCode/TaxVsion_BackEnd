import { describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting, type MeetingSnapshot } from '../../src/domain/meetings/meeting.js';
import type { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
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
  async findById(): Promise<Meeting | null> {
    return null;
  }
  async findByShortCode(): Promise<null> {
    return null;
  }
  async findByShortCodeAnyTenant(): Promise<null> {
    return null;
  }
  async saveInvitation(): Promise<void> {}
  async findInvitationByHash(): Promise<null> {
    return null;
  }
  async findInvitationById(): Promise<null> {
    return null;
  }
  async listInvitationsByMeeting(): Promise<MeetingInvitation[]> {
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
});
