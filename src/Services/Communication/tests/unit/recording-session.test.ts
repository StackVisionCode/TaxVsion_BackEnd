import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { RecordingSession, type RecordingSessionSnapshot } from '../../src/domain/recording/recording-session.js';
import { RecordingConsentEntry } from '../../src/domain/recording/recording-consent-entry.js';
import { RecordingScope, RecordingSessionState } from '../../src/domain/recording/recording-session-state.js';
import { evaluateRecordingConsentPolicy, RecordingConsentPolicy } from '../../src/domain/recording/recording-consent.js';

function u(): string {
  return randomUUID();
}

function requested() {
  const tenantId = u();
  const scopeId = u();
  const requestedByUserId = u();
  const r = RecordingSession.request({ tenantId, scope: RecordingScope.Meeting, scopeId, requestedByUserId });
  if (!r.isSuccess) throw new Error();
  return { session: r.value, tenantId, scopeId, requestedByUserId };
}

/** Fase Backend 2 no expone un camino publico a Processing — se rehidrata directo para probar complete/fail en aislamiento. */
function processingSession(): RecordingSession {
  const snapshot: RecordingSessionSnapshot = {
    id: u(),
    tenantId: u(),
    scope: RecordingScope.Meeting,
    scopeId: u(),
    state: RecordingSessionState.Processing,
    requestedByUserId: u(),
    requestedAtUtc: new Date(),
    startedAtUtc: new Date(),
    stoppedAtUtc: new Date(),
    recordingFileId: null,
    durationSeconds: null,
    failureReason: null,
  };
  return RecordingSession.rehydrate(snapshot);
}

describe('RecordingSession.request', () => {
  it('creates a session in Requesting state', () => {
    const { session, tenantId, scopeId, requestedByUserId } = requested();
    const snap = session.toSnapshot();
    expect(snap.state).toBe('Requesting');
    expect(snap.tenantId).toBe(tenantId);
    expect(snap.scopeId).toBe(scopeId);
    expect(snap.requestedByUserId).toBe(requestedByUserId);
    expect(snap.startedAtUtc).toBeNull();
    expect(snap.recordingFileId).toBeNull();
  });
});

describe('RecordingSession.start', () => {
  it('transitions Requesting -> Recording when policy allows', () => {
    const { session } = requested();
    const r = session.start({ policyAllows: true });
    expect(r.isSuccess).toBe(true);
    expect(session.sessionState).toBe('Recording');
    expect(session.toSnapshot().startedAtUtc).not.toBeNull();
  });

  it('fails and stays Requesting when policy blocks', () => {
    const { session } = requested();
    const r = session.start({ policyAllows: false });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('RecordingSession.ConsentPolicyBlocked');
    expect(session.sessionState).toBe('Requesting');
  });

  it('fails from a non-Requesting state', () => {
    const { session } = requested();
    session.start({ policyAllows: true });
    const r = session.start({ policyAllows: true });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('RecordingSession.InvalidTransition');
  });
});

describe('RecordingSession.stop', () => {
  it('transitions Recording -> Stopping', () => {
    const { session } = requested();
    session.start({ policyAllows: true });
    const r = session.stop();
    expect(r.isSuccess).toBe(true);
    expect(session.sessionState).toBe('Stopping');
    expect(session.toSnapshot().stoppedAtUtc).not.toBeNull();
  });

  it('fails when not Recording', () => {
    const { session } = requested();
    const r = session.stop();
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('RecordingSession.InvalidTransition');
  });
});

describe('RecordingSession.fail', () => {
  it.each(['Requesting', 'Recording', 'Stopping', 'Processing'] as const)(
    'transitions %s -> Failed with a reason',
    (from) => {
      const { session } = requested();
      if (from !== 'Requesting') session.start({ policyAllows: true });
      if (from === 'Stopping' || from === 'Processing') session.stop();
      // Processing no es alcanzable publicamente en Fase 2; simulamos moviendo
      // el estado interno via rehydrate para ese caso especifico.
      const target = from === 'Processing' ? processingSession() : session;
      const r = target.fail({ reason: 'ffmpeg exited 1' });
      expect(r.isSuccess).toBe(true);
      expect(target.sessionState).toBe('Failed');
      expect(target.toSnapshot().failureReason).toBe('ffmpeg exited 1');
    },
  );

  it('fails when already terminal', () => {
    const { session } = requested();
    session.start({ policyAllows: true });
    session.fail({ reason: 'first failure' });
    const r = session.fail({ reason: 'second failure' });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('RecordingSession.InvalidTransition');
  });
});

describe('RecordingSession.complete', () => {
  it('transitions Processing -> Ready with fileId and duration', () => {
    const session = processingSession();
    const r = session.complete({ recordingFileId: u(), durationSeconds: 120 });
    expect(r.isSuccess).toBe(true);
    expect(session.sessionState).toBe('Ready');
    expect(session.toSnapshot().durationSeconds).toBe(120);
  });

  it('fails when not Processing', () => {
    const { session } = requested();
    const r = session.complete({ recordingFileId: u(), durationSeconds: 10 });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('RecordingSession.InvalidTransition');
  });

  it('rejects negative duration', () => {
    const session = processingSession();
    const r = session.complete({ recordingFileId: u(), durationSeconds: -1 });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('RecordingSession.InvalidDuration');
  });
});

describe('RecordingConsentEntry.record', () => {
  it('creates an append-only entry', () => {
    const r = RecordingConsentEntry.record({
      tenantId: u(),
      scope: RecordingScope.Call,
      scopeId: u(),
      userId: u(),
      response: 'Accepted',
      respondedAtUtc: new Date(),
    });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) {
      expect(r.value.response).toBe('Accepted');
      expect(r.value.toSnapshot().recordedAtUtc).toBeInstanceOf(Date);
    }
  });

  it('rejects an invalid response value at runtime', () => {
    const r = RecordingConsentEntry.record({
      tenantId: u(),
      scope: RecordingScope.Meeting,
      scopeId: u(),
      userId: u(),
      // @ts-expect-error probando el guard runtime contra datos no confiables
      response: 'Maybe',
      respondedAtUtc: new Date(),
    });
    expect(r.isSuccess).toBe(false);
  });
});

describe('evaluateRecordingConsentPolicy', () => {
  const requestedBy = u();
  const alice = u();
  const bob = u();
  const participantUserIds = [requestedBy, alice, bob];

  it('HostOverride always allows regardless of responses', () => {
    const allows = evaluateRecordingConsentPolicy({
      policy: RecordingConsentPolicy.HostOverride,
      participantUserIds,
      requestedByUserId: requestedBy,
      consentEntries: [{ userId: alice, response: 'Rejected' }],
    });
    expect(allows).toBe(true);
  });

  it('NoRejections allows when nobody has rejected (no-response = accepted by default)', () => {
    const allows = evaluateRecordingConsentPolicy({
      policy: RecordingConsentPolicy.NoRejections,
      participantUserIds,
      requestedByUserId: requestedBy,
      consentEntries: [{ userId: alice, response: 'Accepted' }],
    });
    expect(allows).toBe(true);
  });

  it('NoRejections blocks on a single explicit rejection', () => {
    const allows = evaluateRecordingConsentPolicy({
      policy: RecordingConsentPolicy.NoRejections,
      participantUserIds,
      requestedByUserId: requestedBy,
      consentEntries: [{ userId: alice, response: 'Accepted' }, { userId: bob, response: 'Rejected' }],
    });
    expect(allows).toBe(false);
  });

  it('AllAcceptedRequired blocks unless every other participant explicitly accepted', () => {
    const partial = evaluateRecordingConsentPolicy({
      policy: RecordingConsentPolicy.AllAcceptedRequired,
      participantUserIds,
      requestedByUserId: requestedBy,
      consentEntries: [{ userId: alice, response: 'Accepted' }],
    });
    expect(partial).toBe(false);

    const complete = evaluateRecordingConsentPolicy({
      policy: RecordingConsentPolicy.AllAcceptedRequired,
      participantUserIds,
      requestedByUserId: requestedBy,
      consentEntries: [
        { userId: alice, response: 'Accepted' },
        { userId: bob, response: 'Accepted' },
      ],
    });
    expect(complete).toBe(true);
  });

  it('excludes the requester from the participants that must respond', () => {
    const allows = evaluateRecordingConsentPolicy({
      policy: RecordingConsentPolicy.AllAcceptedRequired,
      participantUserIds: [requestedBy],
      requestedByUserId: requestedBy,
      consentEntries: [],
    });
    expect(allows).toBe(true);
  });
});
