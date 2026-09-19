import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { MeetingSnapshot } from '../../src/domain/meetings/meeting.js';
import type { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import { leaveMeeting } from '../../src/application/use-cases/leave-meeting.js';

function u(): string {
  return randomUUID();
}

class FakeMeetingRepository implements MeetingRepository {
  private readonly meetings = new Map<string, Meeting>();
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
  async saveInvitation(): Promise<void> {}
  async findInvitationByHash(): Promise<MeetingInvitation | null> {
    return null;
  }
  async findInvitationById(): Promise<MeetingInvitation | null> {
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
  async getStatsForUser(): Promise<{ today: number; thisWeek: number; liveNow: number; transcriptsAvailable: number }> {
    return { today: 0, thisWeek: 0, liveNow: 0, transcriptsAvailable: 0 };
  }
}

class FakePublisher implements IntegrationEventPublisher {
  readonly events: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.events.push(event);
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

/** Meeting Live con host (Joined) + un attendee admitido (Joined), sin sala de espera. */
async function liveMeetingWithTwoJoined() {
  const tenantId = u();
  const host = { userId: u(), displayName: 'Host' };
  const attendeeUserId = u();
  const scheduled = Meeting.schedule({ tenantId, title: 'Consulta', host });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  meeting.requestJoin({ userId: attendeeUserId, displayName: 'Cliente', hasValidInvitation: false, passcodeMatch: null });
  meeting.admit({ hostUserId: host.userId, targetUserId: attendeeUserId });
  return { tenantId, meeting, host, attendeeUserId };
}

describe('leaveMeeting use-case', () => {
  it('returns the leaving participant as a DTO with status Left (so the handler can prune the roster)', async () => {
    const { tenantId, meeting, attendeeUserId } = await liveMeetingWithTwoJoined();
    const meetings = new FakeMeetingRepository();
    await meetings.save(meeting);
    const deps = { meetings, publisher: new FakePublisher(), conversations: new FakeConversationRepository() };

    const result = await leaveMeeting(
      { tenantId, correlationId: 'test', meetingId: meeting.id, userId: attendeeUserId },
      deps,
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value.participant.userId).toBe(attendeeUserId);
    expect(result.value.participant.status).toBe('Left');
    // El roster ya lo tiene como Left (lo que los demás clientes usan para podar el tile).
    const left = meeting.toSnapshot().participants.find((p) => p.userId === attendeeUserId);
    expect(left?.status).toBe('Left');
  });
});
