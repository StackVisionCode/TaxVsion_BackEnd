import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import type { MeetingSnapshot } from '../../src/domain/meetings/meeting.js';
import { RecordingSession } from '../../src/domain/recording/recording-session.js';
import { RecordingScope } from '../../src/domain/recording/recording-session-state.js';
import { RecordingConsentEntry } from '../../src/domain/recording/recording-consent-entry.js';
import { RecordingConsentPolicy } from '../../src/domain/recording/recording-consent.js';
import type { RecordingSessionRepository, RecordingConsentRepository } from '../../src/application/ports/recording-repository.js';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';
import type { SettingsRepository, LimitsRepository } from '../../src/application/ports/settings-repository.js';
import type { TenantCommunicationLimitsSnapshot } from '../../src/domain/settings/tenant-communication-limits.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { startMeetingRecording } from '../../src/application/use-cases/start-meeting-recording.js';

function u(): string {
  return randomUUID();
}

class FakeMeetingRepository implements MeetingRepository {
  private readonly store = new Map<string, Meeting>();
  async save(meeting: Meeting): Promise<void> {
    this.store.set(meeting.id, meeting);
  }
  async findById(tenantId: string, meetingId: string): Promise<Meeting | null> {
    const m = this.store.get(meetingId);
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
}

class FakeRecordingSessionRepository implements RecordingSessionRepository {
  private readonly store = new Map<string, RecordingSession>();
  private key(scope: string, scopeId: string): string {
    return `${scope}:${scopeId}`;
  }
  async save(session: RecordingSession): Promise<void> {
    const s = session.toSnapshot();
    this.store.set(this.key(s.scope, s.scopeId), session);
  }
  async findByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    const session = this.store.get(this.key(scope, scopeId));
    if (!session) return null;
    return session.toSnapshot().tenantId === tenantId ? session : null;
  }
  async listStaleRequesting(olderThanUtc: Date, scope?: RecordingScope): Promise<RecordingSession[]> {
    return [...this.store.values()].filter((session) => {
      const snap = session.toSnapshot();
      if (snap.state !== 'Requesting') return false;
      if (snap.requestedAtUtc.getTime() > olderThanUtc.getTime()) return false;
      if (scope && snap.scope !== scope) return false;
      return true;
    });
  }
}

class FakeRecordingConsentRepository implements RecordingConsentRepository {
  private readonly rows: RecordingConsentEntry[] = [];
  async append(entry: RecordingConsentEntry): Promise<void> {
    this.rows.push(entry);
  }
  async listByScope(tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingConsentEntry[]> {
    return this.rows.filter((row) => {
      const s = row.toSnapshot();
      return s.tenantId === tenantId && s.scope === scope && s.scopeId === scopeId;
    });
  }
}

class FakeSettingsRepository implements SettingsRepository {
  constructor(private readonly policy: RecordingConsentPolicy) {}
  async findByTenantId(tenantId: string): Promise<TenantCommunicationSettings | null> {
    const settings = TenantCommunicationSettings.defaults(tenantId);
    settings.update({ recordingConsentPolicy: this.policy });
    return settings;
  }
  async save(): Promise<void> {}
  async listPurgeEnabled(): Promise<Array<{ tenantId: string; messageRetentionDays: number }>> {
    return [];
  }
}

// Requerido por el tipo LimitsRepository pero no usado en estos tests.
class FakeLimitsRepository implements LimitsRepository {
  async findByTenantId(): Promise<TenantCommunicationLimitsSnapshot | null> {
    return null;
  }
  async upsert(): Promise<void> {}
  async markSuspended(): Promise<void> {}
}
void FakeLimitsRepository;

class FakePublisher implements IntegrationEventPublisher {
  readonly events: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.events.push(event);
  }
}

class FakeEmitter implements RealtimeEmitter {
  readonly emitted: Array<{ event: string; payload: unknown }> = [];
  emitToConversation(): void {}
  emitToCall(): void {}
  emitToMeeting(input: { event: string; envelope: { payload: unknown } }): void {
    this.emitted.push({ event: input.event, payload: input.envelope.payload });
  }
  emitToUser(): void {}
  emitToTenant(): void {}
}

function liveMeetingWithTwoParticipants() {
  const host = { userId: u(), displayName: 'Host' };
  const attendeeUserId = u();
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Consulta', host });
  if (!scheduled.isSuccess) throw new Error();
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  meeting.requestJoin({ userId: attendeeUserId, displayName: 'Cliente', hasValidInvitation: false, passcodeMatch: null });
  meeting.admit({ hostUserId: host.userId, targetUserId: attendeeUserId });
  return { meeting, host, attendeeUserId };
}

function buildHarness(policy: RecordingConsentPolicy) {
  const meetings = new FakeMeetingRepository();
  const recordingSessions = new FakeRecordingSessionRepository();
  const recordingConsents = new FakeRecordingConsentRepository();
  const tenantSettings = new FakeSettingsRepository(policy);
  const publisher = new FakePublisher();
  const emitter = new FakeEmitter();
  return { meetings, recordingSessions, recordingConsents, tenantSettings, publisher, emitter };
}

async function seedRequestingSession(
  harness: ReturnType<typeof buildHarness>,
  meeting: Meeting,
  requestedByUserId: string,
) {
  await harness.meetings.save(meeting);
  const requestResult = meeting.requestRecording({ actorUserId: requestedByUserId, existingSession: null });
  if (!requestResult.isSuccess) throw new Error(requestResult.error.message);
  await harness.recordingSessions.save(requestResult.value.session);
  return requestResult.value.session;
}

describe('startMeetingRecording — policy AllAcceptedRequired', () => {
  it('does NOT start when one participant rejects', async () => {
    const harness = buildHarness(RecordingConsentPolicy.AllAcceptedRequired);
    const { meeting, host, attendeeUserId } = liveMeetingWithTwoParticipants();
    await seedRequestingSession(harness, meeting, host.userId);

    const consentResult = meeting.recordConsent({ userId: attendeeUserId, response: 'Rejected', respondedAtUtc: new Date() });
    if (!consentResult.isSuccess) throw new Error();
    await harness.recordingConsents.append(consentResult.value);

    const result = await startMeetingRecording(
      { tenantId: meeting.tenantId, correlationId: u(), meetingId: meeting.id, actorUserId: host.userId },
      harness,
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('RecordingSession.ConsentPolicyNotSatisfied');

    const session = await harness.recordingSessions.findByScope(meeting.tenantId, RecordingScope.Meeting, meeting.id);
    expect(session?.sessionState).toBe('Requesting');
    expect(harness.publisher.events).toHaveLength(0);
  });

  it('starts once every other participant explicitly accepted', async () => {
    const harness = buildHarness(RecordingConsentPolicy.AllAcceptedRequired);
    const { meeting, host, attendeeUserId } = liveMeetingWithTwoParticipants();
    await seedRequestingSession(harness, meeting, host.userId);

    const consentResult = meeting.recordConsent({ userId: attendeeUserId, response: 'Accepted', respondedAtUtc: new Date() });
    if (!consentResult.isSuccess) throw new Error();
    await harness.recordingConsents.append(consentResult.value);

    const result = await startMeetingRecording(
      { tenantId: meeting.tenantId, correlationId: u(), meetingId: meeting.id, actorUserId: host.userId },
      harness,
    );
    expect(result.isSuccess).toBe(true);

    const session = await harness.recordingSessions.findByScope(meeting.tenantId, RecordingScope.Meeting, meeting.id);
    expect(session?.sessionState).toBe('Recording');
    expect(harness.publisher.events).toHaveLength(1);
    expect(harness.publisher.events[0]?.eventType).toBe('communication.meeting.recording_started.v1');
    expect(harness.emitter.emitted.some((e) => e.event === 'meeting.recording.state_changed')).toBe(true);
  });
});

describe('startMeetingRecording — policy NoRejections (default)', () => {
  it('starts even with zero explicit responses (no-response = accepted by default)', async () => {
    const harness = buildHarness(RecordingConsentPolicy.NoRejections);
    const { meeting, host } = liveMeetingWithTwoParticipants();
    await seedRequestingSession(harness, meeting, host.userId);

    const result = await startMeetingRecording(
      { tenantId: meeting.tenantId, correlationId: u(), meetingId: meeting.id, actorUserId: host.userId },
      harness,
    );
    expect(result.isSuccess).toBe(true);

    const session = await harness.recordingSessions.findByScope(meeting.tenantId, RecordingScope.Meeting, meeting.id);
    expect(session?.sessionState).toBe('Recording');
  });

  it('does NOT start once a participant explicitly rejects', async () => {
    const harness = buildHarness(RecordingConsentPolicy.NoRejections);
    const { meeting, host, attendeeUserId } = liveMeetingWithTwoParticipants();
    await seedRequestingSession(harness, meeting, host.userId);

    const consentResult = meeting.recordConsent({ userId: attendeeUserId, response: 'Rejected', respondedAtUtc: new Date() });
    if (!consentResult.isSuccess) throw new Error();
    await harness.recordingConsents.append(consentResult.value);

    const result = await startMeetingRecording(
      { tenantId: meeting.tenantId, correlationId: u(), meetingId: meeting.id, actorUserId: host.userId },
      harness,
    );
    expect(result.isSuccess).toBe(false);
  });
});

describe('recording-consent-timeout-scheduler behavior (via startMeetingRecording directly)', () => {
  it('a stale Requesting session with NoRejections and no responses resolves to Recording — same call the scheduler makes on timeout', async () => {
    const harness = buildHarness(RecordingConsentPolicy.NoRejections);
    const { meeting, host } = liveMeetingWithTwoParticipants();
    const session = await seedRequestingSession(harness, meeting, host.userId);

    // Simula que la sesion ya tiene mas de 30s (lo que el scheduler filtraria via listStaleRequesting).
    const stale = await harness.recordingSessions.listStaleRequesting(
      new Date(session.toSnapshot().requestedAtUtc.getTime() + 1),
      RecordingScope.Meeting,
    );
    expect(stale).toHaveLength(1);

    // El scheduler llama exactamente esto para cada sesion stale, usando el
    // requestedByUserId original como actor (ya validado Host/Cohost al pedir).
    const result = await startMeetingRecording(
      {
        tenantId: session.toSnapshot().tenantId,
        correlationId: u(),
        meetingId: session.scopeId,
        actorUserId: session.requestedByUserId,
      },
      harness,
    );
    expect(result.isSuccess).toBe(true);
    const after = await harness.recordingSessions.findByScope(meeting.tenantId, RecordingScope.Meeting, meeting.id);
    expect(after?.sessionState).toBe('Recording');
  });

  it('a stale Requesting session with AllAcceptedRequired and no responses stays blocked — scheduler must fail it, not start it', async () => {
    const harness = buildHarness(RecordingConsentPolicy.AllAcceptedRequired);
    const { meeting, host } = liveMeetingWithTwoParticipants();
    const session = await seedRequestingSession(harness, meeting, host.userId);

    const result = await startMeetingRecording(
      {
        tenantId: session.toSnapshot().tenantId,
        correlationId: u(),
        meetingId: session.scopeId,
        actorUserId: session.requestedByUserId,
      },
      harness,
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('RecordingSession.ConsentPolicyNotSatisfied');
  });
});
