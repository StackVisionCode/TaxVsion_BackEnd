import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { Call } from '../../src/domain/calls/call.js';
import { RecordingSession, type RecordingSessionSnapshot } from '../../src/domain/recording/recording-session.js';
import {
  RecordingCaptureMode,
  RecordingScope,
  RecordingSessionState,
  RecordingStopReason,
} from '../../src/domain/recording/recording-session-state.js';
import type { TranscriptArtifact } from '../../src/domain/recording/transcript-artifact.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { TranscriptArtifactRepository } from '../../src/application/ports/transcript-artifact-repository.js';
import type { CloudStorageMetadataClient } from '../../src/application/ports/cloudstorage-metadata-client.js';
import type { RecordingCaptureService } from '../../src/application/services/recording-capture.service.js';
import { Result } from '../../src/domain/shared/result.js';
import {
  failRecording,
  handleRecordingComposed,
  markRecordingFileAvailable,
  markRecordingFileRejected,
  stopRecordingBySystem,
  type RecordingLifecycleDeps,
} from '../../src/application/use-cases/recording-lifecycle.js';
import { runRecordingSweep } from '../../src/application/use-cases/recording-sweep.js';
import { InMemoryRecordingSessionRepository, F7_SNAPSHOT_DEFAULTS } from '../helpers/fake-recording-session-repository.js';
import {
  InMemoryMeetingEventLog,
  InMemoryMeetingRepository,
  RecordingEmitter,
  RecordingPublisher,
  RecordingSfu,
} from '../helpers/fake-meeting-runtime.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';

const u = () => randomUUID();

class InMemoryCalls {
  readonly calls = new Map<string, Call>();
  async save(call: Call): Promise<void> {
    this.calls.set(call.id, call);
  }
  async findById(tenantId: string, callId: string): Promise<Call | null> {
    const c = this.calls.get(callId);
    return c && c.tenantId === tenantId ? c : null;
  }
  asRepository(): CallRepository {
    return this as unknown as CallRepository;
  }
}

class InMemoryTranscripts implements TranscriptArtifactRepository {
  readonly items = new Map<string, TranscriptArtifact>();
  async save(artifact: TranscriptArtifact): Promise<void> {
    const s = artifact.toSnapshot();
    this.items.set(`${s.scope}:${s.scopeId}`, artifact);
  }
  async findByScope(_tenantId: string, scope: string, scopeId: string): Promise<TranscriptArtifact | null> {
    return this.items.get(`${scope}:${scopeId}`) ?? null;
  }
}

/** Captura falsa: registra lo que se le pide y siempre acepta. */
function fakeCapture() {
  const calls: string[] = [];
  const capture = {
    available: true,
    begin: async () => {
      calls.push('begin');
      return Result.okVoid();
    },
    stop: async (_session: RecordingSession, reason: string) => {
      calls.push(`stop:${reason}`);
      return Result.okVoid();
    },
    abort: async () => {
      calls.push('abort');
    },
    recompose: async () => Result.okVoid(),
  } as unknown as RecordingCaptureService;
  return { capture, calls };
}

function runtime() {
  const meetings = new InMemoryMeetingRepository();
  const calls = new InMemoryCalls();
  const recordingSessions = new InMemoryRecordingSessionRepository();
  const transcripts = new InMemoryTranscripts();
  const publisher = new RecordingPublisher();
  const emitter = new RecordingEmitter();
  const meetingEvents = new InMemoryMeetingEventLog();
  const sfu = new RecordingSfu();
  const { capture, calls: captureCalls } = fakeCapture();
  const deps: RecordingLifecycleDeps = {
    meetings: meetings.asRepository(),
    calls: calls.asRepository(),
    recordingSessions,
    publisher,
    emitter,
    meetingEvents,
    effectiveSettings: new FakeEffectiveSettingsResolver(),
    capture,
    transcripts,
    sfu: { getRouterRtpCapabilities: async () => ({ codecs: [] }), closeMeeting: (id: string) => sfu.closeMeeting(id) },
  };
  return { meetings, calls, recordingSessions, transcripts, publisher, emitter, meetingEvents, sfu, captureCalls, deps };
}

function liveMeeting() {
  const host = { userId: u(), displayName: 'Host Name' };
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Review', host });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  return { meeting, host };
}

function activeCall() {
  const caller = { userId: u(), displayName: 'Ana' };
  const callee = { userId: u(), displayName: 'Bob' };
  const initiated = Call.initiate({ tenantId: u(), kind: 'Video', caller, callee });
  if (!initiated.isSuccess) throw new Error('initiate failed');
  const call = initiated.value;
  call.accept({ byUserId: callee.userId, calleeDisplayName: callee.displayName });
  call.markActive();
  return { call, caller, callee };
}

function serverSession(input: {
  tenantId: string;
  scope: RecordingScope;
  scopeId: string;
  requestedByUserId: string;
  state: RecordingSessionState;
  patch?: Partial<RecordingSessionSnapshot>;
}): RecordingSession {
  return RecordingSession.rehydrate({
    id: u(),
    tenantId: input.tenantId,
    scope: input.scope,
    scopeId: input.scopeId,
    state: input.state,
    requestedByUserId: input.requestedByUserId,
    requestedAtUtc: new Date(Date.now() - 600_000),
    startedAtUtc: new Date(Date.now() - 300_000),
    stoppedAtUtc: input.state === RecordingSessionState.Recording ? null : new Date(Date.now() - 120_000),
    recordingFileId: null,
    durationSeconds: null,
    failureReason: null,
    ...F7_SNAPSHOT_DEFAULTS,
    captureMode: RecordingCaptureMode.Server,
    ...input.patch,
  });
}

describe('RecordingSession (server capture)', () => {
  it('starts with a capture mode and a limit, warns once and reports the limit', () => {
    const requested = RecordingSession.request({ tenantId: u(), scope: RecordingScope.Meeting, scopeId: u(), requestedByUserId: u() });
    if (!requested.isSuccess) throw new Error();
    const session = requested.value;
    const startedAt = new Date('2026-10-30T10:00:00Z');
    expect(session.start({ policyAllows: true, now: startedAt, captureMode: RecordingCaptureMode.Server, maxDurationSeconds: 600 }).isSuccess).toBe(true);
    expect(session.limitAtUtc()?.toISOString()).toBe('2026-10-30T10:10:00.000Z');

    const beforeWindow = new Date('2026-10-30T10:04:00Z');
    const inWindow = new Date('2026-10-30T10:06:00Z');
    expect(session.shouldWarnLimit(beforeWindow, 300)).toBe(false);
    expect(session.shouldWarnLimit(inWindow, 300)).toBe(true);
    session.warnLimit(inWindow);
    expect(session.shouldWarnLimit(inWindow, 300)).toBe(false);
    expect(session.isLimitReached(new Date('2026-10-30T10:10:00Z'))).toBe(true);
  });

  it('rejects a non-positive limit', () => {
    const requested = RecordingSession.request({ tenantId: u(), scope: RecordingScope.Call, scopeId: u(), requestedByUserId: u() });
    if (!requested.isSuccess) throw new Error();
    const r = requested.value.start({ policyAllows: true, maxDurationSeconds: 0 });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('RecordingSession.InvalidLimit');
  });

  it('goes Stopping -> Composing -> Processing -> Ready and keeps the stop reason', () => {
    const session = serverSession({ tenantId: u(), scope: RecordingScope.Meeting, scopeId: u(), requestedByUserId: u(), state: RecordingSessionState.Recording });
    expect(session.stop(new Date(), RecordingStopReason.LimitReached).isSuccess).toBe(true);
    expect(session.toSnapshot().stopReason).toBe('LimitReached');
    expect(session.beginComposing().isSuccess).toBe(true);
    const fileId = u();
    const composed = session.composed({
      composedFileId: fileId,
      composedSizeBytes: 1024,
      format: 'mp4',
      durationSeconds: 42.4,
      manifest: { audioTracks: [], videoTrackCount: 1 },
    });
    expect(composed.isSuccess).toBe(true);
    expect(session.sessionState).toBe('Processing');
    expect(session.toSnapshot().recordingFileId).toBe(fileId);
    expect(session.toSnapshot().durationSeconds).toBe(42);
    expect(session.markReady().isSuccess).toBe(true);
    expect(session.sessionState).toBe('Ready');
  });

  it('never lets the client upload a server-side recording', () => {
    const session = serverSession({ tenantId: u(), scope: RecordingScope.Call, scopeId: u(), requestedByUserId: u(), state: RecordingSessionState.Stopping });
    const r = session.beginProcessing(u());
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('RecordingSession.ServerCapture');
  });

  it('only composes server-side sessions', () => {
    const session = serverSession({
      tenantId: u(),
      scope: RecordingScope.Meeting,
      scopeId: u(),
      requestedByUserId: u(),
      state: RecordingSessionState.Stopping,
      patch: { captureMode: RecordingCaptureMode.Client },
    });
    const r = session.beginComposing();
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('RecordingSession.NotServerCapture');
  });

  it('recomposes a Ready recording unless it was purged', () => {
    const ready = serverSession({ tenantId: u(), scope: RecordingScope.Meeting, scopeId: u(), requestedByUserId: u(), state: RecordingSessionState.Ready, patch: { recordingFileId: u() } });
    expect(ready.beginRecomposing().isSuccess).toBe(true);
    expect(ready.sessionState).toBe('Composing');

    const purged = serverSession({
      tenantId: u(),
      scope: RecordingScope.Meeting,
      scopeId: u(),
      requestedByUserId: u(),
      state: RecordingSessionState.Ready,
      patch: { recordingFileId: u(), purgedAtUtc: new Date() },
    });
    const r = purged.beginRecomposing();
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Recording.TracksPurged');
  });
  it('rescues a recording whose composition failed, but not one rejected by storage', () => {
    const composeFailed = serverSession({ tenantId: u(), scope: RecordingScope.Meeting, scopeId: u(), requestedByUserId: u(), state: RecordingSessionState.Failed, patch: { failureReason: 'ComposeError' } });
    expect(composeFailed.beginRecomposing().isSuccess).toBe(true);
    expect(composeFailed.sessionState).toBe('Composing');
    composeFailed.composed({ composedFileId: u(), composedSizeBytes: 10, format: 'mp4', durationSeconds: 5, manifest: { audioTracks: [], videoTrackCount: 0 } });
    expect(composeFailed.toSnapshot().failureReason).toBeNull();

    const rejected = serverSession({ tenantId: u(), scope: RecordingScope.Meeting, scopeId: u(), requestedByUserId: u(), state: RecordingSessionState.Failed, patch: { failureReason: 'StorageRejected' } });
    expect(rejected.beginRecomposing().isSuccess).toBe(false);
  });
});

describe('recording lifecycle (server capture)', () => {
  it('a system stop releases the capture and moves the session to Composing', async () => {
    const rt = runtime();
    const { meeting, host } = liveMeeting();
    await rt.meetings.save(meeting);
    const session = serverSession({ tenantId: meeting.tenantId, scope: RecordingScope.Meeting, scopeId: meeting.id, requestedByUserId: host.userId, state: RecordingSessionState.Recording });
    await rt.recordingSessions.save(session);

    const r = await stopRecordingBySystem(
      { tenantId: meeting.tenantId, scope: RecordingScope.Meeting, scopeId: meeting.id, reason: RecordingStopReason.MeetingEnded, correlationId: 'c' },
      rt.deps,
    );

    expect(r.isSuccess).toBe(true);
    expect(rt.captureCalls).toContain('stop:MeetingEnded');
    expect(session.sessionState).toBe('Composing');
    expect(rt.publisher.types()).toContain('communication.meeting.recording_stopped.v1');
  });

  it('applies the composed file, points the meeting to it and asks for the transcript with named tracks', async () => {
    const rt = runtime();
    const { meeting, host } = liveMeeting();
    await rt.meetings.save(meeting);
    const session = serverSession({ tenantId: meeting.tenantId, scope: RecordingScope.Meeting, scopeId: meeting.id, requestedByUserId: host.userId, state: RecordingSessionState.Composing });
    await rt.recordingSessions.save(session);
    const composedFileId = u();
    const payload = {
      sessionId: session.id,
      scope: 'Meeting' as const,
      scopeId: meeting.id,
      composedFileId,
      composedSizeBytes: 2048,
      format: 'mp4',
      durationSeconds: 60,
      audioTracks: [{ trackId: u(), userId: host.userId, fileId: u(), source: 'camera' as const, startOffsetMs: 0, durationMs: 60_000 }],
      videoTrackCount: 1,
    };

    await handleRecordingComposed(payload, { tenantId: meeting.tenantId, correlationId: 'c' }, rt.deps);
    // Reentrega del mismo evento: sin efecto adicional.
    await handleRecordingComposed(payload, { tenantId: meeting.tenantId, correlationId: 'c' }, rt.deps);

    expect(session.sessionState).toBe('Processing');
    expect(meeting.toSnapshot().recordingFileId).toBe(composedFileId);
    const processing = rt.publisher.events.filter((e) => e.eventType === 'communication.meeting.recording_processing_started.v1');
    expect(processing).toHaveLength(1);
    const audioTracks = (processing[0] as unknown as { audioTracks: Array<{ displayName: string }> }).audioTracks;
    expect(audioTracks[0]?.displayName).toBe('Host Name');
    expect((await rt.transcripts.findByScope(meeting.tenantId, 'Meeting', meeting.id))?.toSnapshot().state).toBe('Processing');
  });

  it('marks the recording Ready when CloudStorage makes the file available', async () => {
    const rt = runtime();
    const { meeting, host } = liveMeeting();
    await rt.meetings.save(meeting);
    const fileId = u();
    const session = serverSession({
      tenantId: meeting.tenantId,
      scope: RecordingScope.Meeting,
      scopeId: meeting.id,
      requestedByUserId: host.userId,
      state: RecordingSessionState.Processing,
      patch: { recordingFileId: fileId, composedFileId: fileId, durationSeconds: 60 },
    });
    await rt.recordingSessions.save(session);

    await markRecordingFileAvailable({ tenantId: meeting.tenantId, fileId }, rt.deps);

    expect(session.sessionState).toBe('Ready');
    expect(rt.publisher.types()).toContain('communication.meeting.recording_ready.v1');
    expect(rt.emitter.events('meeting')).toContain('meeting.recording.state_changed');
    expect(rt.meetingEvents.types(meeting.id)).toContain('recording.ready');
  });

  it('fails the recording when CloudStorage rejects the file', async () => {
    const rt = runtime();
    const { meeting, host } = liveMeeting();
    await rt.meetings.save(meeting);
    const fileId = u();
    const session = serverSession({
      tenantId: meeting.tenantId,
      scope: RecordingScope.Meeting,
      scopeId: meeting.id,
      requestedByUserId: host.userId,
      state: RecordingSessionState.Processing,
      patch: { recordingFileId: fileId },
    });
    await rt.recordingSessions.save(session);

    await markRecordingFileRejected({ tenantId: meeting.tenantId, fileId, reason: 'Infected' }, rt.deps);

    expect(session.sessionState).toBe('Failed');
    expect(session.toSnapshot().failureReason).toBe('Infected');
    expect(rt.publisher.types()).toContain('communication.meeting.recording_failed.v1');
  });

  it('a failed server-side call recording tells the clients to stop publishing and closes the call router', async () => {
    const rt = runtime();
    const { call, caller } = activeCall();
    await rt.calls.save(call);
    const session = serverSession({ tenantId: call.tenantId, scope: RecordingScope.Call, scopeId: call.id, requestedByUserId: caller.userId, state: RecordingSessionState.Recording });
    await rt.recordingSessions.save(session);

    await failRecording(session, 'RecorderUnavailable', rt.deps, 'c');

    expect(session.sessionState).toBe('Failed');
    expect(rt.captureCalls).toContain('abort');
    expect(rt.emitter.events('call')).toContain('call.recording.capture_stopped');
    expect(rt.sfu.closedMeetings).toEqual([call.id]);
  });
});

describe('recording sweep', () => {
  it('stops a recording that reached its limit', async () => {
    const rt = runtime();
    const { meeting, host } = liveMeeting();
    await rt.meetings.save(meeting);
    const session = serverSession({
      tenantId: meeting.tenantId,
      scope: RecordingScope.Meeting,
      scopeId: meeting.id,
      requestedByUserId: host.userId,
      state: RecordingSessionState.Recording,
      patch: { maxDurationSeconds: 60 },
    });
    await rt.recordingSessions.save(session);
    const metadata: CloudStorageMetadataClient = { getMetadata: async () => null } as unknown as CloudStorageMetadataClient;

    const report = await runRecordingSweep(new Date(), { ...rt.deps, effectiveSettings: new FakeEffectiveSettingsResolver(), cloudStorageMetadata: metadata });

    expect(report.stoppedByLimit).toBe(1);
    expect(session.toSnapshot().stopReason).toBe('LimitReached');
    expect(session.sessionState).toBe('Composing');
  });

  it('warns once before the limit', async () => {
    const rt = runtime();
    const { meeting, host } = liveMeeting();
    await rt.meetings.save(meeting);
    // Empezo hace 300 s con limite de 400 s: quedan 100 s, dentro de la ventana de aviso por defecto.
    const session = serverSession({
      tenantId: meeting.tenantId,
      scope: RecordingScope.Meeting,
      scopeId: meeting.id,
      requestedByUserId: host.userId,
      state: RecordingSessionState.Recording,
      patch: { maxDurationSeconds: 400 },
    });
    await rt.recordingSessions.save(session);
    const metadata = { getMetadata: async () => null } as unknown as CloudStorageMetadataClient;
    const deps = { ...rt.deps, effectiveSettings: new FakeEffectiveSettingsResolver(), cloudStorageMetadata: metadata };

    const first = await runRecordingSweep(new Date(), deps);
    const second = await runRecordingSweep(new Date(), deps);

    expect(first.warned).toBe(1);
    expect(second.warned).toBe(0);
    expect(rt.emitter.events('meeting')).toContain('meeting.recording.limit_warning');
  });

  it('falls back to CloudStorage metadata when the availability event never arrives', async () => {
    const rt = runtime();
    const { meeting, host } = liveMeeting();
    await rt.meetings.save(meeting);
    const fileId = u();
    const session = serverSession({
      tenantId: meeting.tenantId,
      scope: RecordingScope.Meeting,
      scopeId: meeting.id,
      requestedByUserId: host.userId,
      state: RecordingSessionState.Processing,
      patch: { recordingFileId: fileId, composedAtUtc: new Date(Date.now() - 120_000) },
    });
    await rt.recordingSessions.save(session);
    const metadata = { getMetadata: async () => ({ status: 'Available' }) } as unknown as CloudStorageMetadataClient;

    const report = await runRecordingSweep(new Date(), { ...rt.deps, effectiveSettings: new FakeEffectiveSettingsResolver(), cloudStorageMetadata: metadata });

    expect(report.ready).toBe(1);
    expect(session.sessionState).toBe('Ready');
  });

  it('fails with StorageRejected when CloudStorage never registers the file', async () => {
    const rt = runtime();
    const { meeting, host } = liveMeeting();
    await rt.meetings.save(meeting);
    const session = serverSession({
      tenantId: meeting.tenantId,
      scope: RecordingScope.Meeting,
      scopeId: meeting.id,
      requestedByUserId: host.userId,
      state: RecordingSessionState.Processing,
      patch: { recordingFileId: u(), composedAtUtc: new Date(Date.now() - 3 * 3_600_000) },
    });
    await rt.recordingSessions.save(session);
    const metadata = { getMetadata: async () => null } as unknown as CloudStorageMetadataClient;

    const report = await runRecordingSweep(new Date(), { ...rt.deps, effectiveSettings: new FakeEffectiveSettingsResolver(), cloudStorageMetadata: metadata });

    expect(report.failed).toBe(1);
    expect(session.toSnapshot().failureReason).toBe('StorageRejected');
  });
});
