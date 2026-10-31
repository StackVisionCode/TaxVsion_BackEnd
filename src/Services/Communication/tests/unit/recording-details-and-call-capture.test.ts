import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { Call } from '../../src/domain/calls/call.js';
import { RecordingSession } from '../../src/domain/recording/recording-session.js';
import { RecordingCaptureMode, RecordingScope, RecordingSessionState } from '../../src/domain/recording/recording-session-state.js';
import { TranscriptArtifact } from '../../src/domain/recording/transcript-artifact.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { CloudStorageDownloadClient } from '../../src/application/ports/cloudstorage-download-client.js';
import type { CustomerAssignmentProjectionRepository } from '../../src/application/ports/customer-assignment-projection-repository.js';
import type { SfuService } from '../../src/application/ports/sfu-service.js';
import type { TurnCredentialFactory } from '../../src/application/ports/turn-credential-factory.js';
import { getRecordingDetails, type GetRecordingDetailsDeps } from '../../src/application/use-cases/get-recording-details.js';
import { createCallCaptureTransport, produceCallCapture } from '../../src/application/use-cases/call-capture-sfu.js';
import { InMemoryRecordingSessionRepository, F7_SNAPSHOT_DEFAULTS } from '../helpers/fake-recording-session-repository.js';
import { InMemoryMeetingRepository } from '../helpers/fake-meeting-runtime.js';
import { FakeEffectiveSettingsResolver, effectiveDefaults } from '../helpers/fake-effective-settings.js';
import { createFakeProjectionRepository } from '../helpers/fake-user-permissions-projection-repository.js';

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

function session(input: { tenantId: string; scope: RecordingScope; scopeId: string; state: RecordingSessionState; fileId?: string | null; captureMode?: RecordingCaptureMode }) {
  return RecordingSession.rehydrate({
    id: u(),
    tenantId: input.tenantId,
    scope: input.scope,
    scopeId: input.scopeId,
    state: input.state,
    requestedByUserId: u(),
    requestedAtUtc: new Date(),
    startedAtUtc: new Date(Date.now() - 60_000),
    stoppedAtUtc: new Date(),
    recordingFileId: input.fileId ?? null,
    durationSeconds: 60,
    failureReason: null,
    ...F7_SNAPSHOT_DEFAULTS,
    captureMode: input.captureMode ?? RecordingCaptureMode.Server,
    composedFormat: 'mp4',
    composedSizeBytes: 1000,
  });
}

function detailsRuntime(platformPatch: Record<string, unknown> = {}) {
  const meetings = new InMemoryMeetingRepository();
  const calls = new InMemoryCalls();
  const recordingSessions = new InMemoryRecordingSessionRepository();
  const transcripts = new Map<string, TranscriptArtifact>();
  const presigned: string[] = [];
  const deps: GetRecordingDetailsDeps = {
    meetings: meetings.asRepository(),
    calls: calls.asRepository(),
    recordingSessions,
    transcripts: {
      save: async (a) => {
        transcripts.set(a.toSnapshot().scopeId, a);
      },
      findByScope: async (_t, _s, scopeId) => transcripts.get(scopeId) ?? null,
    },
    cloudStorageDownload: {
      getDownloadUrl: async (_tenantId: string, fileId: string) => {
        presigned.push(fileId);
        return { downloadUrl: `https://files.local/${fileId}`, expiresAtUtc: '2026-10-30T12:00:00Z' };
      },
    } as unknown as CloudStorageDownloadClient,
    effectiveSettings: new FakeEffectiveSettingsResolver(platformPatch),
    userPermissions: createFakeProjectionRepository([]),
    customerAssignments: { isAssigned: async () => false } as Pick<CustomerAssignmentProjectionRepository, 'isAssigned'>,
    assignmentVisibilityEnabled: false,
  };
  return { meetings, calls, recordingSessions, transcripts, presigned, deps };
}

function endedMeeting() {
  const host = { userId: u(), displayName: 'Host' };
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Review', host });
  if (!scheduled.isSuccess) throw new Error();
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  return { meeting, host };
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

describe('getRecordingDetails', () => {
  it('gives the host the playback URL and the transcript when everything is Ready', async () => {
    const rt = detailsRuntime();
    const { meeting, host } = endedMeeting();
    await rt.meetings.save(meeting);
    const fileId = u();
    await rt.recordingSessions.save(session({ tenantId: meeting.tenantId, scope: RecordingScope.Meeting, scopeId: meeting.id, state: RecordingSessionState.Ready, fileId }));
    const artifact = TranscriptArtifact.start({ tenantId: meeting.tenantId, scope: 'Meeting', scopeId: meeting.id, recordingSessionId: u() });
    const txt = u();
    const json = u();
    artifact.complete({ txtFileId: txt, jsonFileId: json, language: 'en', speakerCount: 2, wordCount: 120, durationSeconds: 60 } as never);
    rt.transcripts.set(meeting.id, artifact);

    const r = await getRecordingDetails(
      { viewer: { tenantId: meeting.tenantId, userId: host.userId, actorType: 'TenantEmployee', holdsRecordPermission: false }, scope: RecordingScope.Meeting, scopeId: meeting.id },
      rt.deps,
    );

    expect(r.isSuccess).toBe(true);
    if (!r.isSuccess) return;
    expect(r.value.state).toBe('Ready');
    expect(r.value.playback?.url).toBe(`https://files.local/${fileId}`);
    expect(r.value.transcript?.state).toBe('Ready');
    expect(r.value.transcript?.txt?.url).toBe(`https://files.local/${txt}`);
  });

  it('does not presign anything while the recording is still processing', async () => {
    const rt = detailsRuntime();
    const { meeting, host } = endedMeeting();
    await rt.meetings.save(meeting);
    await rt.recordingSessions.save(session({ tenantId: meeting.tenantId, scope: RecordingScope.Meeting, scopeId: meeting.id, state: RecordingSessionState.Composing }));

    const r = await getRecordingDetails(
      { viewer: { tenantId: meeting.tenantId, userId: host.userId, actorType: 'TenantEmployee', holdsRecordPermission: false }, scope: RecordingScope.Meeting, scopeId: meeting.id },
      rt.deps,
    );

    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.playback).toBeNull();
    expect(rt.presigned).toEqual([]);
  });

  it('forbids staff who neither took part nor hold the record permission', async () => {
    const rt = detailsRuntime();
    const { meeting } = endedMeeting();
    await rt.meetings.save(meeting);
    await rt.recordingSessions.save(session({ tenantId: meeting.tenantId, scope: RecordingScope.Meeting, scopeId: meeting.id, state: RecordingSessionState.Ready, fileId: u() }));

    const outsider = { tenantId: meeting.tenantId, userId: u(), actorType: 'TenantEmployee' };
    const denied = await getRecordingDetails({ viewer: { ...outsider, holdsRecordPermission: false }, scope: RecordingScope.Meeting, scopeId: meeting.id }, rt.deps);
    const allowed = await getRecordingDetails({ viewer: { ...outsider, holdsRecordPermission: true }, scope: RecordingScope.Meeting, scopeId: meeting.id }, rt.deps);

    expect(denied.isSuccess).toBe(false);
    if (!denied.isSuccess) expect(denied.error.code).toBe('Meeting.Recording.Forbidden');
    expect(allowed.isSuccess).toBe(true);
  });

  it('hides call recordings from the portal unless the office makes them visible', async () => {
    const rt = detailsRuntime();
    const { call, callee } = activeCall();
    await rt.calls.save(call);
    await rt.recordingSessions.save(session({ tenantId: call.tenantId, scope: RecordingScope.Call, scopeId: call.id, state: RecordingSessionState.Ready, fileId: u() }));

    const viewer = { tenantId: call.tenantId, userId: callee.userId, actorType: 'CustomerPortal', holdsRecordPermission: false };
    const hidden = {
      resolve: async (tenantId: string) => {
        const e = effectiveDefaults(tenantId);
        return { ...e, recording: { ...e.recording, visibleInPortal: false } };
      },
    };

    const visible = await getRecordingDetails({ viewer, scope: RecordingScope.Call, scopeId: call.id }, rt.deps);
    const r = await getRecordingDetails({ viewer, scope: RecordingScope.Call, scopeId: call.id }, { ...rt.deps, effectiveSettings: hidden });

    expect(visible.isSuccess).toBe(true);
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Call.Recording.Forbidden');
  });

  it('answers 404 for a purged or missing recording', async () => {
    const rt = detailsRuntime();
    const { meeting, host } = endedMeeting();
    await rt.meetings.save(meeting);

    const r = await getRecordingDetails(
      { viewer: { tenantId: meeting.tenantId, userId: host.userId, actorType: 'TenantEmployee', holdsRecordPermission: false }, scope: RecordingScope.Meeting, scopeId: meeting.id },
      rt.deps,
    );

    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Meeting.Recording.NotFound');
  });
});

describe('call capture SFU (D-56)', () => {
  function captureRuntime() {
    const calls = new InMemoryCalls();
    const recordingSessions = new InMemoryRecordingSessionRepository();
    const created: string[] = [];
    const sfu = {
      createTransport: async (input: { meetingId: string; direction: string }) => {
        created.push(`${input.meetingId}:${input.direction}`);
        return { id: 't1', iceParameters: {}, iceCandidates: [], dtlsParameters: {}, replacedProducerIds: [] };
      },
      produce: async () => ({ producerId: 'p1' }),
      connectTransport: async () => true,
    } as unknown as SfuService;
    const turn = { issue: () => ({ iceServers: [{ urls: ['turn:local'] }] }) } as unknown as TurnCredentialFactory;
    return { calls, recordingSessions, created, deps: { calls: calls.asRepository(), recordingSessions, sfu, turn } };
  }

  it('lets a participant publish send-only to the call router while it is being recorded on the server', async () => {
    const rt = captureRuntime();
    const { call, caller } = activeCall();
    await rt.calls.save(call);
    await rt.recordingSessions.save(session({ tenantId: call.tenantId, scope: RecordingScope.Call, scopeId: call.id, state: RecordingSessionState.Recording }));

    const transport = await createCallCaptureTransport({ tenantId: call.tenantId, callId: call.id, userId: caller.userId }, rt.deps);
    const produced = await produceCallCapture(
      { tenantId: call.tenantId, callId: call.id, userId: caller.userId, transportId: 't1', kind: 'audio', rtpParameters: {} as never, source: 'camera' },
      rt.deps,
    );

    expect(transport.isSuccess).toBe(true);
    if (transport.isSuccess) {
      expect(transport.value.iceServers).toHaveLength(1);
      expect('replacedProducerIds' in transport.value).toBe(false);
    }
    expect(rt.created).toEqual([`${call.id}:send`]);
    expect(produced.isSuccess).toBe(true);
  });

  it('rejects people outside the call', async () => {
    const rt = captureRuntime();
    const { call } = activeCall();
    await rt.calls.save(call);
    await rt.recordingSessions.save(session({ tenantId: call.tenantId, scope: RecordingScope.Call, scopeId: call.id, state: RecordingSessionState.Recording }));

    const r = await createCallCaptureTransport({ tenantId: call.tenantId, callId: call.id, userId: u() }, rt.deps);

    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Call.NotParticipant');
  });

  it('rejects publishing when the call is not recorded on the server', async () => {
    const rt = captureRuntime();
    const { call, caller } = activeCall();
    await rt.calls.save(call);
    await rt.recordingSessions.save(
      session({ tenantId: call.tenantId, scope: RecordingScope.Call, scopeId: call.id, state: RecordingSessionState.Recording, captureMode: RecordingCaptureMode.Client }),
    );

    const r = await createCallCaptureTransport({ tenantId: call.tenantId, callId: call.id, userId: caller.userId }, rt.deps);

    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Call.Recording.NotCapturing');
    expect(rt.created).toEqual([]);
  });
});
