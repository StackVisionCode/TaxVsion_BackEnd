import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { RecordingSession, type RecordingSessionSnapshot } from '../../src/domain/recording/recording-session.js';
import { RecordingScope, RecordingSessionState } from '../../src/domain/recording/recording-session-state.js';

function u(): string {
  return randomUUID();
}

function liveMeetingWithAttendee() {
  const host = { userId: u(), displayName: 'Host' };
  const scheduleResult = Meeting.schedule({ tenantId: u(), title: 'Consulta', host });
  if (!scheduleResult.isSuccess) throw new Error();
  const meeting = scheduleResult.value;
  meeting.start({ hostUserId: host.userId });
  const attendeeUserId = u();
  meeting.requestJoin({
    userId: attendeeUserId,
    displayName: 'Cliente',
    hasValidInvitation: false,
    passcodeMatch: null,
  });
  // requireWaitingRoom=true por default — sin admitir, el attendee queda
  // Waiting (no Joined) y getJoinedParticipants() lo excluye correctamente.
  meeting.admit({ hostUserId: host.userId, targetUserId: attendeeUserId });
  return { meeting, host, attendeeUserId };
}

function processingSessionFor(meeting: Meeting): RecordingSession {
  const snapshot: RecordingSessionSnapshot = {
    id: u(),
    tenantId: meeting.tenantId,
    scope: RecordingScope.Meeting,
    scopeId: meeting.id,
    state: RecordingSessionState.Processing,
    requestedByUserId: meeting.hostUserId,
    requestedAtUtc: new Date(),
    startedAtUtc: new Date(),
    stoppedAtUtc: new Date(),
    recordingFileId: null,
    durationSeconds: null,
    failureReason: null,
  };
  return RecordingSession.rehydrate(snapshot);
}

describe('Meeting.requestRecording', () => {
  it('host can request recording; returns a Requesting session and the joined participant ids', () => {
    const { meeting, host, attendeeUserId } = liveMeetingWithAttendee();
    const r = meeting.requestRecording({ actorUserId: host.userId, existingSession: null });
    expect(r.isSuccess).toBe(true);
    if (!r.isSuccess) return;
    expect(r.value.session.sessionState).toBe('Requesting');
    expect(r.value.session.scope).toBe('Meeting');
    expect(r.value.session.scopeId).toBe(meeting.id);
    expect(r.value.participantUserIds).toEqual(expect.arrayContaining([host.userId, attendeeUserId]));
  });

  it('rejects non-host/non-cohost actors', () => {
    const { meeting } = liveMeetingWithAttendee();
    const r = meeting.requestRecording({ actorUserId: u(), existingSession: null });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Meeting.HostOnly');
  });

  it('rejects a second request when a session already exists for this meeting', () => {
    const { meeting, host } = liveMeetingWithAttendee();
    const first = meeting.requestRecording({ actorUserId: host.userId, existingSession: null });
    if (!first.isSuccess) throw new Error();
    const second = meeting.requestRecording({ actorUserId: host.userId, existingSession: first.value.session });
    expect(second.isSuccess).toBe(false);
    if (!second.isSuccess) expect(second.error.code).toBe('Meeting.Recording.AlreadyRequested');
  });
});

describe('Meeting.recordConsent', () => {
  it('records a response for a known participant', () => {
    const { meeting, attendeeUserId } = liveMeetingWithAttendee();
    const r = meeting.recordConsent({ userId: attendeeUserId, response: 'Accepted', respondedAtUtc: new Date() });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.response).toBe('Accepted');
  });

  it('rejects a user who never participated in this meeting', () => {
    const { meeting } = liveMeetingWithAttendee();
    const r = meeting.recordConsent({ userId: u(), response: 'Accepted', respondedAtUtc: new Date() });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Meeting.NotParticipant');
  });
});

describe('Meeting.startRecording / stopRecording', () => {
  it('host starts recording when policy allows', () => {
    const { meeting, host } = liveMeetingWithAttendee();
    const requested = meeting.requestRecording({ actorUserId: host.userId, existingSession: null });
    if (!requested.isSuccess) throw new Error();
    const r = meeting.startRecording({ actorUserId: host.userId, session: requested.value.session, policyAllows: true });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.sessionState).toBe('Recording');
  });

  it('rejects starting when actor is not host/cohost', () => {
    const { meeting, host } = liveMeetingWithAttendee();
    const requested = meeting.requestRecording({ actorUserId: host.userId, existingSession: null });
    if (!requested.isSuccess) throw new Error();
    const r = meeting.startRecording({ actorUserId: u(), session: requested.value.session, policyAllows: true });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Meeting.HostOnly');
  });

  it('rejects a session that belongs to a different meeting', () => {
    const { meeting: meetingA, host: hostA } = liveMeetingWithAttendee();
    const { meeting: meetingB, host: hostB } = liveMeetingWithAttendee();
    const requestedOnB = meetingB.requestRecording({ actorUserId: hostB.userId, existingSession: null });
    if (!requestedOnB.isSuccess) throw new Error();
    const r = meetingA.startRecording({
      actorUserId: hostA.userId,
      session: requestedOnB.value.session,
      policyAllows: true,
    });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Meeting.Recording.SessionMismatch');
  });

  it('host stops a Recording session', () => {
    const { meeting, host } = liveMeetingWithAttendee();
    const requested = meeting.requestRecording({ actorUserId: host.userId, existingSession: null });
    if (!requested.isSuccess) throw new Error();
    meeting.startRecording({ actorUserId: host.userId, session: requested.value.session, policyAllows: true });
    const r = meeting.stopRecording({ actorUserId: host.userId, session: requested.value.session });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.sessionState).toBe('Stopping');
  });
});

describe('Meeting.failRecording / completeRecording', () => {
  it('fails a session without requiring a specific actor', () => {
    const { meeting, host } = liveMeetingWithAttendee();
    const requested = meeting.requestRecording({ actorUserId: host.userId, existingSession: null });
    if (!requested.isSuccess) throw new Error();
    const r = meeting.failRecording({ session: requested.value.session, reason: 'worker crashed' });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) {
      expect(r.value.sessionState).toBe('Failed');
      expect(r.value.toSnapshot().failureReason).toBe('worker crashed');
    }
  });

  it('completes a Processing session with the final file and duration', () => {
    const { meeting } = liveMeetingWithAttendee();
    const processing = processingSessionFor(meeting);
    const fileId = u();
    const r = meeting.completeRecording({ session: processing, recordingFileId: fileId, durationSeconds: 300 });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) {
      expect(r.value.sessionState).toBe('Ready');
      expect(r.value.toSnapshot().recordingFileId).toBe(fileId);
    }
  });

  it('rejects completing a session that belongs to another meeting', () => {
    const { meeting: meetingA } = liveMeetingWithAttendee();
    const { meeting: meetingB } = liveMeetingWithAttendee();
    const processingOnB = processingSessionFor(meetingB);
    const r = meetingA.completeRecording({ session: processingOnB, recordingFileId: u(), durationSeconds: 10 });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Meeting.Recording.SessionMismatch');
  });
});

/**
 * El transcript llega via un pipeline asincronico (download+ffmpeg+
 * whisper.cpp) que puede tardar mas de lo que el host tarda en cerrar el
 * meeting — antes attachTranscript solo aceptaba Ended, y TranscriptReady
 * llegando con el meeting todavia Live quedaba huerfano permanentemente
 * (Meeting.Transcript.InvalidState, warn-and-drop sin retry en el consumer).
 */
describe('Meeting.attachTranscript', () => {
  it('attaches the transcript while the meeting is still Live', () => {
    const { meeting } = liveMeetingWithAttendee();
    const fileId = u();
    const r = meeting.attachTranscript(fileId);
    expect(r.isSuccess).toBe(true);
    expect(meeting.toSnapshot().transcriptFileId).toBe(fileId);
  });

  it('attaches the transcript after the meeting has Ended', () => {
    const { meeting, host } = liveMeetingWithAttendee();
    meeting.end({ byUserId: host.userId });
    const fileId = u();
    const r = meeting.attachTranscript(fileId);
    expect(r.isSuccess).toBe(true);
    expect(meeting.toSnapshot().transcriptFileId).toBe(fileId);
  });

  it('rejects attaching before the meeting starts (still Scheduled)', () => {
    const host = { userId: u(), displayName: 'Host' };
    const scheduleResult = Meeting.schedule({ tenantId: u(), title: 'Consulta', host });
    if (!scheduleResult.isSuccess) throw new Error();
    const r = scheduleResult.value.attachTranscript(u());
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Meeting.Transcript.InvalidState');
  });

  it('rejects attaching a second transcript', () => {
    const { meeting } = liveMeetingWithAttendee();
    meeting.attachTranscript(u());
    const r = meeting.attachTranscript(u());
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Meeting.Transcript.Duplicate');
  });
});
