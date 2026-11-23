import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import {
  markRecordingFileAvailable,
  markRecordingFileRejected,
  type RecordingLifecycleDeps,
} from '../../src/application/use-cases/recording-lifecycle.js';
import { bindTranscriptConsumers } from '../../src/application/event-handlers/transcript-consumers.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { TranscriptArtifactRepository } from '../../src/application/ports/transcript-artifact-repository.js';
import type { TranscriptArtifact } from '../../src/domain/recording/transcript-artifact.js';
import type { FileSourceDeclaredEvent } from '../../src/contracts/events/cloudstorage-events.js';
import { RecordingScope, RecordingSessionState } from '../../src/domain/recording/recording-session-state.js';
import type { Call } from '../../src/domain/calls/call.js';
import type { Meeting } from '../../src/domain/meetings/meeting.js';
import { InMemoryRecordingSessionRepository } from '../helpers/fake-recording-session-repository.js';
import { InMemoryMeetingEventLog, RecordingPublisher } from '../helpers/fake-meeting-runtime.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';
import { callWith, entitiesOf, meetingWith, sessionWith } from '../helpers/recording-file-library.js';

const u = () => randomUUID();
const DECLARED = 'cloudstorage.file_source_declared.v1';

class InMemoryTranscripts implements TranscriptArtifactRepository {
  readonly items: TranscriptArtifact[] = [];
  async save(artifact: TranscriptArtifact): Promise<void> {
    if (!this.items.includes(artifact)) this.items.push(artifact);
  }
  async findByScope(_t: string, scope: string, scopeId: string): Promise<TranscriptArtifact | null> {
    return (
      this.items.find((a) => a.toSnapshot().scope === scope && a.toSnapshot().scopeId === scopeId) ?? null
    );
  }
  async findByRecordingSession(_t: string, id: string): Promise<TranscriptArtifact | null> {
    return this.items.find((a) => a.toSnapshot().recordingSessionId === id) ?? null;
  }
}

function runtime(entity: Meeting | Call) {
  const recordingSessions = new InMemoryRecordingSessionRepository();
  const publisher = new RecordingPublisher();
  const transcripts = new InMemoryTranscripts();
  const deps: RecordingLifecycleDeps = {
    ...entitiesOf(entity),
    recordingSessions,
    publisher,
    meetingEvents: new InMemoryMeetingEventLog(),
    effectiveSettings: new FakeEffectiveSettingsResolver(),
    transcripts,
  };
  const declared = () =>
    publisher.events.filter((e): e is FileSourceDeclaredEvent => e.eventType === DECLARED);
  return { recordingSessions, publisher, transcripts, deps, declared };
}

describe('Grabaciones — declaran su origen en CloudStorage', () => {
  it('una grabación de reunión que queda Ready declara meeting · recording', async () => {
    const meeting = meetingWith({ tenantId: u(), hostUserId: u() });
    const rt = runtime(meeting);
    const fileId = u();
    await rt.recordingSessions.save(
      sessionWith({
        tenantId: meeting.tenantId,
        scope: RecordingScope.Meeting,
        scopeId: meeting.id,
        state: RecordingSessionState.Processing,
        fileId,
      }),
    );

    await markRecordingFileAvailable({ tenantId: meeting.tenantId, fileId, correlationId: 'corr' }, rt.deps);

    expect(rt.declared()).toEqual([
      expect.objectContaining({
        tenantId: meeting.tenantId,
        fileId,
        sourceService: 'communication',
        sourceResourceType: 'meeting',
        sourceResourceId: meeting.id,
        sourceRole: 'recording',
        relatedCustomerId: null,
      }),
    ]);
  });

  // El correo de "grabación lista" nombra la reunión: el evento lleva su título.
  it('el aviso de grabación lista lleva el título de la reunión', async () => {
    const meeting = meetingWith({ tenantId: u(), hostUserId: u(), title: 'Quarterly review' });
    const rt = runtime(meeting);
    const fileId = u();
    await rt.recordingSessions.save(
      sessionWith({
        tenantId: meeting.tenantId,
        scope: RecordingScope.Meeting,
        scopeId: meeting.id,
        state: RecordingSessionState.Processing,
        fileId,
      }),
    );

    await markRecordingFileAvailable({ tenantId: meeting.tenantId, fileId }, rt.deps);

    const ready = rt.publisher.events.find((e) => e.eventType === 'communication.meeting.recording_ready.v1');
    expect(ready).toEqual(expect.objectContaining({ meetingTitle: 'Quarterly review' }));
  });

  it('una llamada con un cliente declara call · recording con ese cliente', async () => {
    const customerId = u();
    const call = callWith({
      tenantId: u(),
      callerUserId: u(),
      calleeUserId: u(),
      calleeCustomerId: customerId,
    });
    const rt = runtime(call);
    const fileId = u();
    await rt.recordingSessions.save(
      sessionWith({
        tenantId: call.tenantId,
        scope: RecordingScope.Call,
        scopeId: call.id,
        state: RecordingSessionState.Processing,
        fileId,
      }),
    );

    await markRecordingFileAvailable({ tenantId: call.tenantId, fileId }, rt.deps);

    expect(
      rt.declared().map((d) => [d.sourceResourceType, d.sourceRole, d.sourceResourceId, d.relatedCustomerId]),
    ).toEqual([['call', 'recording', call.id, customerId]]);
  });

  it('una grabación rechazada (Failed) no declara nada', async () => {
    const meeting = meetingWith({ tenantId: u(), hostUserId: u() });
    const rt = runtime(meeting);
    const fileId = u();
    await rt.recordingSessions.save(
      sessionWith({
        tenantId: meeting.tenantId,
        scope: RecordingScope.Meeting,
        scopeId: meeting.id,
        state: RecordingSessionState.Processing,
        fileId,
      }),
    );

    await markRecordingFileRejected({ tenantId: meeting.tenantId, fileId, reason: 'Infected' }, rt.deps);

    expect(rt.declared()).toEqual([]);
  });

  it('el transcript listo declara el .txt como transcript y el .json como transcript_data', async () => {
    const call = callWith({ tenantId: u(), callerUserId: u(), calleeUserId: u() });
    const rt = runtime(call);
    const recordingFileId = u();
    await rt.recordingSessions.save(
      sessionWith({
        tenantId: call.tenantId,
        scope: RecordingScope.Call,
        scopeId: call.id,
        state: RecordingSessionState.Ready,
        fileId: recordingFileId,
      }),
    );
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    bindTranscriptConsumers((type, handler) => handlers.set(type, handler), {
      calls: rt.deps.calls,
      meetings: rt.deps.meetings,
      recordingSessions: rt.recordingSessions,
      publisher: rt.publisher,
      emitter: { emitToCall: () => undefined, emitToMeeting: () => undefined } as unknown as RealtimeEmitter,
      transcripts: rt.transcripts,
    });
    const [txt, json] = [u(), u()];

    await handlers.get('communication.call.transcript_ready.v1')?.({
      eventId: u(),
      eventType: 'communication.call.transcript_ready.v1',
      tenantId: call.tenantId,
      correlationId: 'corr',
      occurredOnUtc: new Date().toISOString(),
      payload: { callId: call.id, transcriptFileId: txt, transcriptJsonFileId: json, recordingFileId },
    } as IncomingEnvelope);

    expect(
      rt.declared().map((d) => [d.fileId, d.sourceResourceType, d.sourceRole, d.sourceResourceId]),
    ).toEqual([
      [txt, 'call', 'transcript', call.id],
      [json, 'call', 'transcript_data', call.id],
    ]);
  });
});
