import { describe, it, expect, vi, beforeEach } from 'vitest';
import { randomUUID } from 'node:crypto';
import type { CloudStorageClient } from '../../src/cloudstorage/cloudstorage-client.js';
import type { RecordingReadyEvent } from '../../src/rabbit/consumer.js';
import { pipelineFailuresTotal, pipelineDurationSeconds } from '../../src/telemetry/metrics.js';

/**
 * Fase Transcript 8 — confirma que `processRecordingReady` (no solo el
 * modulo de metrics en aislamiento, ver metrics.test.ts) efectivamente
 * incrementa `pipelineFailuresTotal` en cada falla y registra una
 * observacion en `pipelineDurationSeconds` con el `status` correcto, tanto
 * en el camino feliz como en una falla real.
 */

const mocks = vi.hoisted(() => ({
  publishTranscriptReady: vi.fn(),
  publishTranscriptFailed: vi.fn(),
  transcodeToWav16kMono: vi.fn(),
  probeAudioStreams: vi.fn(),
  transcribeWav: vi.fn(),
}));

vi.mock('../../src/rabbit/publisher.js', () => ({
  publishTranscriptReady: mocks.publishTranscriptReady,
  publishTranscriptFailed: mocks.publishTranscriptFailed,
}));
vi.mock('../../src/media/audio-transcoder.js', () => ({
  transcodeToWav16kMono: mocks.transcodeToWav16kMono,
  probeAudioStreams: mocks.probeAudioStreams,
}));
vi.mock('../../src/whisper/whisper-transcriber.js', () => ({
  transcribeWav: mocks.transcribeWav,
}));

const { processRecordingReady } = await import('../../src/pipeline.js');

function makeEvent(overrides: Partial<RecordingReadyEvent> = {}): RecordingReadyEvent {
  return {
    kind: 'meeting',
    eventId: randomUUID(),
    tenantId: randomUUID(),
    correlationId: randomUUID(),
    targetId: randomUUID(),
    recordingFileId: randomUUID(),
    ...overrides,
  };
}

function makeCloudStorage(overrides: {
  downloadFile?: () => Promise<void>;
  uploadFile?: () => Promise<{ fileId: string }>;
}): CloudStorageClient {
  return {
    downloadFile: overrides.downloadFile ?? (async () => undefined),
    uploadFile: overrides.uploadFile ?? (async () => ({ fileId: randomUUID() })),
  } as unknown as CloudStorageClient;
}

beforeEach(() => {
  vi.clearAllMocks();
  pipelineFailuresTotal.reset();
  pipelineDurationSeconds.reset();
  mocks.probeAudioStreams.mockResolvedValue(1);
  mocks.transcodeToWav16kMono.mockResolvedValue(undefined);
  mocks.transcribeWav.mockResolvedValue({
    text: 'hola mundo',
    detectedLanguage: 'es',
    durationSeconds: 5,
    wordCount: 2,
  });
  mocks.publishTranscriptReady.mockReturnValue(undefined);
});

describe('processRecordingReady — metricas (Fase Transcript 8)', () => {
  it('camino feliz: registra 1 observacion de duracion con status=success, sin incrementar failures', async () => {
    const event = makeEvent({ kind: 'meeting' });
    const cloudStorage = makeCloudStorage({});

    await processRecordingReady(event, { cloudStorage });

    const failures = await pipelineFailuresTotal.get();
    expect(failures.values.length).toBe(0);

    const duration = await pipelineDurationSeconds.get();
    const successCount = duration.values.find(
      (v) =>
        v.metricName === 'transcript_worker_pipeline_duration_seconds_count' &&
        v.labels.kind === 'meeting' &&
        v.labels.status === 'success',
    );
    expect(successCount?.value).toBe(1);
  });

  it('download failure: incrementa pipelineFailuresTotal(reason=DownloadFailed, kind=call) y registra duracion con status=failure', async () => {
    const event = makeEvent({ kind: 'call' });
    const downloadErr = new Error('download-url request failed with status 500');
    const downloadFile = vi.fn(() => Promise.reject(downloadErr));
    const cloudStorage = makeCloudStorage({ downloadFile });

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow(downloadErr);

    const failures = await pipelineFailuresTotal.get();
    const downloadFailure = failures.values.find(
      (v) => v.labels.reason === 'DownloadFailed' && v.labels.kind === 'call',
    );
    expect(downloadFailure?.value).toBe(1);

    const duration = await pipelineDurationSeconds.get();
    const failureCount = duration.values.find(
      (v) =>
        v.metricName === 'transcript_worker_pipeline_duration_seconds_count' &&
        v.labels.kind === 'call' &&
        v.labels.status === 'failure',
    );
    expect(failureCount?.value).toBe(1);
  });

  it('no audio stream: incrementa pipelineFailuresTotal(reason=NoAudioStream)', async () => {
    const event = makeEvent({ kind: 'meeting' });
    mocks.probeAudioStreams.mockRejectedValueOnce(new Error('ffprobe reported 0 audio streams'));
    const cloudStorage = makeCloudStorage({});

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow();

    const failures = await pipelineFailuresTotal.get();
    const noAudioFailure = failures.values.find(
      (v) => v.labels.reason === 'NoAudioStream' && v.labels.kind === 'meeting',
    );
    expect(noAudioFailure?.value).toBe(1);
  });
});
