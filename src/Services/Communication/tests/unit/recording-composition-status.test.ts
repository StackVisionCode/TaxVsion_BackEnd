import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { RecordingSession } from '../../src/domain/recording/recording-session.js';
import { RecordingCaptureMode, RecordingScope, RecordingSessionState } from '../../src/domain/recording/recording-session-state.js';
import { RecordingCaptureService } from '../../src/application/services/recording-capture.service.js';
import { RecorderRequestError, type RecorderClient, type RecorderSessionStatus } from '../../src/application/ports/recorder-client.js';
import type { SfuService } from '../../src/application/ports/sfu-service.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import { InMemoryMeetingRepository } from '../helpers/fake-meeting-runtime.js';
import { InMemoryRecordingSessionRepository, F7_SNAPSHOT_DEFAULTS } from '../helpers/fake-recording-session-repository.js';

const u = () => randomUUID();

function composingSession(): RecordingSession {
  return RecordingSession.rehydrate({
    id: u(),
    tenantId: u(),
    scope: RecordingScope.Meeting,
    scopeId: u(),
    state: RecordingSessionState.Composing,
    requestedByUserId: u(),
    requestedAtUtc: new Date(),
    startedAtUtc: new Date(),
    stoppedAtUtc: new Date(),
    recordingFileId: null,
    durationSeconds: null,
    failureReason: null,
    ...F7_SNAPSHOT_DEFAULTS,
    captureMode: RecordingCaptureMode.Server,
  });
}

function recorderState(state: string, result: unknown = null): RecorderSessionStatus {
  return { state, jobId: 'job-1', failure: null, composedAtUtc: null, retainUntilUtc: null, updatedAtUtc: null, trackCount: 2, result };
}

function serviceWith(getSession: RecorderClient['getSession'], configured = true): RecordingCaptureService {
  return new RecordingCaptureService({
    recorder: { configured, getSession } as unknown as RecorderClient,
    sfu: {} as SfuService,
    recordingSessions: new InMemoryRecordingSessionRepository(),
    meetings: new InMemoryMeetingRepository().asRepository(),
    calls: {} as CallRepository,
    resolveContext: async () => {
      throw new Error('not used');
    },
    tapListenIp: '127.0.0.1',
    keyframeIntervalSeconds: 0,
  });
}

describe('RecordingCaptureService.compositionStatus', () => {
  it.each([
    ['queued', 'working'],
    ['composing', 'working'],
    ['failed', 'failed'],
    ['aborted', 'gone'],
    ['capturing', 'unknown'],
  ])('el recorder en "%s" se informa como "%s"', async (state, expected) => {
    const report = await serviceWith(async () => recorderState(state)).compositionStatus(composingSession());
    expect(report.status).toBe(expected);
  });

  it('sin la sesión en el recorder se informa "gone"', async () => {
    const report = await serviceWith(async () => null).compositionStatus(composingSession());
    expect(report.status).toBe('gone');
  });

  it('terminada: entrega el resultado con los datos de la sesión, listo para aplicarse', async () => {
    const session = composingSession();
    const composedFileId = u();
    const result = { composedFileId, composedSizeBytes: 4096, format: 'mp4', durationSeconds: 9.5, videoTrackCount: 1, audioTracks: [] };

    const report = await serviceWith(async () => recorderState('composed', result)).compositionStatus(session);

    expect(report.status).toBe('done');
    expect(report.composed).toMatchObject({
      sessionId: session.id,
      scope: 'Meeting',
      scopeId: session.scopeId,
      composedFileId,
      durationSeconds: 9.5,
    });
  });

  it('terminada con un resultado ilegible: se informa "done" pero sin nada que aplicar', async () => {
    const report = await serviceWith(async () => recorderState('composed', { composedFileId: 'not-a-uuid' })).compositionStatus(composingSession());
    expect(report.status).toBe('done');
    expect(report.composed).toBeUndefined();
  });

  // Recorder caído, o una versión anterior sin la consulta: no se sabe, y no se lanza.
  it('si el recorder no responde o no conoce la ruta, se informa "unknown" sin lanzar', async () => {
    const down = serviceWith(async () => {
      throw new Error('fetch failed');
    });
    const old = serviceWith(async () => {
      throw new RecorderRequestError('Recording.RouteNotFound', 404, 'Route not found.');
    });
    expect((await down.compositionStatus(composingSession())).status).toBe('unknown');
    expect((await old.compositionStatus(composingSession())).status).toBe('unknown');
    expect((await serviceWith(async () => null, false).compositionStatus(composingSession())).status).toBe('unknown');
  });
});
