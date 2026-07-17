import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { CallSnapshot } from '../../src/domain/calls/call.js';
import { RecordingSession } from '../../src/domain/recording/recording-session.js';
import { RecordingScope } from '../../src/domain/recording/recording-session-state.js';
import { RecordingConsentEntry } from '../../src/domain/recording/recording-consent-entry.js';
import type { RecordingSessionRepository, RecordingConsentRepository } from '../../src/application/ports/recording-repository.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { startCallRecording } from '../../src/application/use-cases/start-call-recording.js';

function u(): string {
  return randomUUID();
}

class FakeCallRepository implements CallRepository {
  private readonly store = new Map<string, Call>();
  async save(call: Call): Promise<void> {
    this.store.set(call.id, call);
  }
  async findById(tenantId: string, callId: string): Promise<Call | null> {
    const c = this.store.get(callId);
    return c && c.tenantId === tenantId ? c : null;
  }
  async findRingingOlderThan(): Promise<CallSnapshot[]> {
    return [];
  }
  async listRecentForUser(): Promise<CallSnapshot[]> {
    return [];
  }
  async countRecentForUser(): Promise<number> {
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

class FakePublisher implements IntegrationEventPublisher {
  readonly events: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.events.push(event);
  }
}

class FakeEmitter implements RealtimeEmitter {
  readonly emitted: Array<{ event: string; payload: unknown }> = [];
  emitToConversation(): void {}
  emitToCall(input: { event: string; envelope: { payload: unknown } }): void {
    this.emitted.push({ event: input.event, payload: input.envelope.payload });
  }
  emitToMeeting(): void {}
  emitToUser(): void {}
  emitToTenant(): void {}
}

function activeCallWithTwoParticipants() {
  const caller = { userId: u(), displayName: 'Caller' };
  const callee = { userId: u(), displayName: 'Callee' };
  const initiated = Call.initiate({ tenantId: u(), kind: 'Audio', caller, callee });
  if (!initiated.isSuccess) throw new Error();
  const call = initiated.value;
  const acceptResult = call.accept({ byUserId: callee.userId, calleeDisplayName: callee.displayName });
  if (!acceptResult.isSuccess) throw new Error();
  const activeResult = call.markActive();
  if (!activeResult.isSuccess) throw new Error();
  return { call, caller, callee };
}

function buildHarness() {
  const calls = new FakeCallRepository();
  const recordingSessions = new FakeRecordingSessionRepository();
  const recordingConsents = new FakeRecordingConsentRepository();
  const publisher = new FakePublisher();
  const emitter = new FakeEmitter();
  return { calls, recordingSessions, recordingConsents, publisher, emitter };
}

async function seedRequestingSession(harness: ReturnType<typeof buildHarness>, call: Call, requestedByUserId: string) {
  await harness.calls.save(call);
  const requestResult = call.requestRecording({ actorUserId: requestedByUserId, existingSession: null });
  if (!requestResult.isSuccess) throw new Error(requestResult.error.message);
  await harness.recordingSessions.save(requestResult.value.session);
  return requestResult.value.session;
}

describe('startCallRecording — policy fija AllAcceptedRequired', () => {
  it('does NOT start when one participant rejects', async () => {
    const harness = buildHarness();
    const { call, caller, callee } = activeCallWithTwoParticipants();
    await seedRequestingSession(harness, call, caller.userId);

    const consentResult = call.recordConsent({ userId: callee.userId, response: 'Rejected', respondedAtUtc: new Date() });
    if (!consentResult.isSuccess) throw new Error();
    await harness.recordingConsents.append(consentResult.value);

    const result = await startCallRecording(
      { tenantId: call.tenantId, correlationId: u(), callId: call.id, actorUserId: caller.userId },
      harness,
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('RecordingSession.ConsentPolicyNotSatisfied');

    const session = await harness.recordingSessions.findByScope(call.tenantId, RecordingScope.Call, call.id);
    expect(session?.sessionState).toBe('Requesting');
    expect(harness.publisher.events).toHaveLength(0);
  });

  it('does NOT start with zero responses (no-response is NOT accepted by default, unlike meetings NoRejections)', async () => {
    const harness = buildHarness();
    const { call, caller } = activeCallWithTwoParticipants();
    await seedRequestingSession(harness, call, caller.userId);

    const result = await startCallRecording(
      { tenantId: call.tenantId, correlationId: u(), callId: call.id, actorUserId: caller.userId },
      harness,
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('RecordingSession.ConsentPolicyNotSatisfied');
  });

  it('starts once every participant explicitly accepted', async () => {
    const harness = buildHarness();
    const { call, caller, callee } = activeCallWithTwoParticipants();
    await seedRequestingSession(harness, call, caller.userId);

    const callerConsent = call.recordConsent({ userId: caller.userId, response: 'Accepted', respondedAtUtc: new Date() });
    if (!callerConsent.isSuccess) throw new Error();
    await harness.recordingConsents.append(callerConsent.value);

    const calleeConsent = call.recordConsent({ userId: callee.userId, response: 'Accepted', respondedAtUtc: new Date() });
    if (!calleeConsent.isSuccess) throw new Error();
    await harness.recordingConsents.append(calleeConsent.value);

    const result = await startCallRecording(
      { tenantId: call.tenantId, correlationId: u(), callId: call.id, actorUserId: caller.userId },
      harness,
    );
    expect(result.isSuccess).toBe(true);

    const session = await harness.recordingSessions.findByScope(call.tenantId, RecordingScope.Call, call.id);
    expect(session?.sessionState).toBe('Recording');
    expect(harness.publisher.events).toHaveLength(1);
    expect(harness.publisher.events[0]?.eventType).toBe('communication.call.recording_started.v1');
    expect(harness.emitter.emitted.some((e) => e.event === 'call.recording.state_changed')).toBe(true);
  });
});

describe('recording-consent-timeout-scheduler behavior for calls (via startCallRecording directly, 15s timeout)', () => {
  it('a stale Requesting session detected by listStaleRequesting is found scoped to Call only', async () => {
    const harness = buildHarness();
    const { call, caller } = activeCallWithTwoParticipants();
    const session = await seedRequestingSession(harness, call, caller.userId);

    // Simula que la sesion ya tiene mas de 15s (lo que el scheduler filtraria via listStaleRequesting).
    const stale = await harness.recordingSessions.listStaleRequesting(
      new Date(session.toSnapshot().requestedAtUtc.getTime() + 1),
      RecordingScope.Call,
    );
    expect(stale).toHaveLength(1);
  });

  it('with AllAcceptedRequired and incomplete consent, timeout always fails — no "starts by default" path unlike meetings NoRejections', async () => {
    const harness = buildHarness();
    const { call, caller, callee } = activeCallWithTwoParticipants();
    const session = await seedRequestingSession(harness, call, caller.userId);

    // Solo una de las 2 partes acepto — el scheduler llamaria esto al timeout.
    const callerConsent = call.recordConsent({ userId: caller.userId, response: 'Accepted', respondedAtUtc: new Date() });
    if (!callerConsent.isSuccess) throw new Error();
    await harness.recordingConsents.append(callerConsent.value);
    void callee;

    const result = await startCallRecording(
      { tenantId: session.toSnapshot().tenantId, correlationId: u(), callId: session.scopeId, actorUserId: session.requestedByUserId },
      harness,
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('RecordingSession.ConsentPolicyNotSatisfied');

    // El scheduler entonces marcaria failRecording — verificamos que el dominio lo permite desde aca.
    const failResult = call.failRecording({ session, reason: 'ConsentTimeout' });
    expect(failResult.isSuccess).toBe(true);
  });
});
