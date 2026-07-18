import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';
import { RecordingSession, type RecordingSessionSnapshot } from '../../src/domain/recording/recording-session.js';
import { RecordingScope, RecordingSessionState } from '../../src/domain/recording/recording-session-state.js';

function u(): string {
  return randomUUID();
}

function activeCall() {
  const caller = { userId: u(), displayName: 'Ana' };
  const callee = { userId: u(), displayName: 'Bob' };
  const initiated = Call.initiate({ tenantId: u(), kind: 'Video', caller, callee });
  if (!initiated.isSuccess) throw new Error();
  const call = initiated.value;
  call.accept({ byUserId: callee.userId, calleeDisplayName: callee.displayName });
  call.markActive();
  return { call, caller, callee };
}

function processingSessionFor(call: Call): RecordingSession {
  const snapshot: RecordingSessionSnapshot = {
    id: u(),
    tenantId: call.tenantId,
    scope: RecordingScope.Call,
    scopeId: call.id,
    state: RecordingSessionState.Processing,
    requestedByUserId: call.callerUserId,
    requestedAtUtc: new Date(),
    startedAtUtc: new Date(),
    stoppedAtUtc: new Date(),
    recordingFileId: null,
    durationSeconds: null,
    failureReason: null,
  };
  return RecordingSession.rehydrate(snapshot);
}

describe('Call.requestRecording', () => {
  it('caller or callee can request; returns both participant ids', () => {
    const { call, caller, callee } = activeCall();
    const r = call.requestRecording({ actorUserId: caller.userId, existingSession: null });
    expect(r.isSuccess).toBe(true);
    if (!r.isSuccess) return;
    expect(r.value.session.sessionState).toBe('Requesting');
    expect(r.value.session.scope).toBe('Call');
    expect(r.value.participantUserIds).toEqual(expect.arrayContaining([caller.userId, callee.userId]));
  });

  it('rejects a non-participant', () => {
    const { call } = activeCall();
    const r = call.requestRecording({ actorUserId: u(), existingSession: null });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Call.NotParticipant');
  });

  it('rejects a second request when a session already exists for this call', () => {
    const { call, caller } = activeCall();
    const first = call.requestRecording({ actorUserId: caller.userId, existingSession: null });
    if (!first.isSuccess) throw new Error();
    const second = call.requestRecording({ actorUserId: caller.userId, existingSession: first.value.session });
    expect(second.isSuccess).toBe(false);
    if (!second.isSuccess) expect(second.error.code).toBe('Call.Recording.AlreadyRequested');
  });
});

describe('Call.recordConsent', () => {
  it('records a response from the callee', () => {
    const { call, callee } = activeCall();
    const r = call.recordConsent({ userId: callee.userId, response: 'Rejected', respondedAtUtc: new Date() });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.response).toBe('Rejected');
  });

  it('rejects a non-participant', () => {
    const { call } = activeCall();
    const r = call.recordConsent({ userId: u(), response: 'Accepted', respondedAtUtc: new Date() });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Call.NotParticipant');
  });
});

describe('Call.startRecording / stopRecording', () => {
  it('starts when policy allows (AllAcceptedRequired satisfied by caller)', () => {
    const { call, caller } = activeCall();
    const requested = call.requestRecording({ actorUserId: caller.userId, existingSession: null });
    if (!requested.isSuccess) throw new Error();
    const r = call.startRecording({ actorUserId: caller.userId, session: requested.value.session, policyAllows: true });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.sessionState).toBe('Recording');
  });

  it('rejects a session belonging to a different call', () => {
    const { call: callA, caller: callerA } = activeCall();
    const { call: callB, caller: callerB } = activeCall();
    const requestedOnB = callB.requestRecording({ actorUserId: callerB.userId, existingSession: null });
    if (!requestedOnB.isSuccess) throw new Error();
    const r = callA.startRecording({
      actorUserId: callerA.userId,
      session: requestedOnB.value.session,
      policyAllows: true,
    });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Call.Recording.SessionMismatch');
  });

  it('stops a Recording session', () => {
    const { call, caller } = activeCall();
    const requested = call.requestRecording({ actorUserId: caller.userId, existingSession: null });
    if (!requested.isSuccess) throw new Error();
    call.startRecording({ actorUserId: caller.userId, session: requested.value.session, policyAllows: true });
    const r = call.stopRecording({ actorUserId: caller.userId, session: requested.value.session });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.sessionState).toBe('Stopping');
  });
});

describe('Call.failRecording / completeRecording', () => {
  it('fails a session without requiring a specific actor', () => {
    const { call, caller } = activeCall();
    const requested = call.requestRecording({ actorUserId: caller.userId, existingSession: null });
    if (!requested.isSuccess) throw new Error();
    const r = call.failRecording({ session: requested.value.session, reason: 'no audio stream' });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.sessionState).toBe('Failed');
  });

  it('completes a Processing session', () => {
    const { call } = activeCall();
    const processing = processingSessionFor(call);
    const fileId = u();
    const r = call.completeRecording({ session: processing, recordingFileId: fileId, durationSeconds: 45 });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) {
      expect(r.value.sessionState).toBe('Ready');
      expect(r.value.toSnapshot().recordingFileId).toBe(fileId);
    }
  });
});

/**
 * El transcript llega via un pipeline asincronico (download+ffmpeg+
 * whisper.cpp) que puede tardar mas de lo que la call tarda en cortarse —
 * antes attachTranscript solo aceptaba Ended, y TranscriptReady llegando
 * con la call todavia Active quedaba huerfano permanentemente
 * (Call.Transcript.InvalidState, warn-and-drop sin retry en el consumer).
 */
describe('Call.attachTranscript', () => {
  it('attaches the transcript while the call is still Active', () => {
    const { call } = activeCall();
    const fileId = u();
    const r = call.attachTranscript(fileId);
    expect(r.isSuccess).toBe(true);
    expect(call.toSnapshot().transcriptFileId).toBe(fileId);
  });

  it('attaches the transcript after the call has Ended', () => {
    const { call, caller } = activeCall();
    call.end({ byUserId: caller.userId });
    const fileId = u();
    const r = call.attachTranscript(fileId);
    expect(r.isSuccess).toBe(true);
    expect(call.toSnapshot().transcriptFileId).toBe(fileId);
  });

  it('rejects attaching a second transcript', () => {
    const { call } = activeCall();
    call.attachTranscript(u());
    const r = call.attachTranscript(u());
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Call.Transcript.Duplicate');
  });
});
