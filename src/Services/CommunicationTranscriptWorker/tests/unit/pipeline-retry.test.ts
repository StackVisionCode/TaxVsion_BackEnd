import { describe, it, expect, vi, beforeEach, afterAll } from 'vitest';
import { randomUUID } from 'node:crypto';
import type { CloudStorageClient } from '../../src/cloudstorage/cloudstorage-client.js';
import type { RecordingReadyEvent } from '../../src/rabbit/consumer.js';

/**
 * Fase Transcript 4 — retry con backoff (produccion: 1s/5s/30s, hasta 4
 * intentos totales) para download (5xx) y upload (codigos de red/5xx
 * retriables). El backoff es configurable por env (config.ts) precisamente
 * para poder acortarlo aca a milisegundos — probar el backoff de produccion
 * tal cual tardaria ~36s reales por caso agotado.
 *
 * Las 2 env vars se pisan ANTES del `await import('../../src/pipeline.js')`
 * (import dinamico: config.ts recien se evalua en ese momento) y se
 * restauran en `afterAll` para no filtrar el override a otros archivos de
 * test que puedan compartir el mismo worker de Vitest.
 */

const ORIGINAL_MAX_ATTEMPTS = process.env['TRANSCRIPT_WORKER_RETRY_MAX_ATTEMPTS'];
const ORIGINAL_BACKOFF_MS = process.env['TRANSCRIPT_WORKER_RETRY_BACKOFF_MS'];
process.env['TRANSCRIPT_WORKER_RETRY_MAX_ATTEMPTS'] = '4';
process.env['TRANSCRIPT_WORKER_RETRY_BACKOFF_MS'] = '5,10,15';

afterAll(() => {
  if (ORIGINAL_MAX_ATTEMPTS === undefined) delete process.env['TRANSCRIPT_WORKER_RETRY_MAX_ATTEMPTS'];
  else process.env['TRANSCRIPT_WORKER_RETRY_MAX_ATTEMPTS'] = ORIGINAL_MAX_ATTEMPTS;
  if (ORIGINAL_BACKOFF_MS === undefined) delete process.env['TRANSCRIPT_WORKER_RETRY_BACKOFF_MS'];
  else process.env['TRANSCRIPT_WORKER_RETRY_BACKOFF_MS'] = ORIGINAL_BACKOFF_MS;
});

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

// Otros archivos de test tambien importan pipeline.js/config.js dinamicamente
// y pueden compartir el registro de modulos de este mismo worker de Vitest —
// sin resetModules() aca, el import de abajo devolveria el modulo YA
// cacheado (con los defaults de produccion 1s/5s/30s horneados adentro),
// ignorando el override de env de arriba.
vi.resetModules();

const { processRecordingReady } = await import('../../src/pipeline.js');
// DownloadStatusError debe venir de la MISMA instancia de modulo que
// pipeline.ts importo despues del resetModules() de arriba — si se importara
// desde un `import` estatico normal (resuelto ANTES del reset), seria una
// clase DISTINTA a nivel de identidad de modulo, y `err instanceof
// DownloadStatusError` adentro de pipeline.ts siempre daria false.
const { DownloadStatusError } = await import('../../src/cloudstorage/cloudstorage-client.js');

function u(): string {
  return randomUUID();
}

function makeEvent(overrides: Partial<RecordingReadyEvent> = {}): RecordingReadyEvent {
  return {
    kind: 'meeting',
    eventId: u(),
    tenantId: u(),
    correlationId: u(),
    targetId: u(),
    recordingFileId: u(),
    ...overrides,
  };
}

function makeCloudStorage(overrides: {
  downloadFile?: () => Promise<void>;
  uploadFile?: () => Promise<{ fileId: string }>;
}): CloudStorageClient {
  return {
    downloadFile: overrides.downloadFile ?? (async () => undefined),
    uploadFile: overrides.uploadFile ?? (async () => ({ fileId: u() })),
  } as unknown as CloudStorageClient;
}

beforeEach(() => {
  vi.clearAllMocks();
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

describe('processRecordingReady — retry con backoff (Fase Transcript 4)', () => {
  it('download 503 se reintenta 3 veces y falla en el 4to intento -> TranscriptFailed(DownloadFailed)', async () => {
    const event = makeEvent();
    const downloadFile = vi.fn(() =>
      Promise.reject(new DownloadStatusError(503, 'presigned download failed with status 503')),
    );
    const cloudStorage = makeCloudStorage({ downloadFile });

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow(
      'presigned download failed with status 503',
    );

    expect(downloadFile).toHaveBeenCalledTimes(4);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({ failureReason: 'DownloadFailed', errorMessage: expect.stringContaining('503') }),
    );
  });

  it('download 503 se recupera en el 2do intento -> pipeline exitoso, sin TranscriptFailed', async () => {
    const event = makeEvent();
    let call = 0;
    const downloadFile = vi.fn(() => {
      call += 1;
      if (call === 1) return Promise.reject(new DownloadStatusError(503, 'blip'));
      return Promise.resolve(undefined);
    });
    const cloudStorage = makeCloudStorage({ downloadFile });

    await processRecordingReady(event, { cloudStorage });

    expect(downloadFile).toHaveBeenCalledTimes(2);
    expect(mocks.publishTranscriptFailed).not.toHaveBeenCalled();
    expect(mocks.publishTranscriptReady).toHaveBeenCalledTimes(1);
  });

  it('download con status 404 (no retriable) -> un solo intento, falla de inmediato', async () => {
    const event = makeEvent();
    const downloadFile = vi.fn(() => Promise.reject(new DownloadStatusError(404, 'file not found (404)')));
    const cloudStorage = makeCloudStorage({ downloadFile });

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow('file not found (404)');

    expect(downloadFile).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({ failureReason: 'DownloadFailed' }),
    );
  });

  it('upload con error de red retriable (ECONNRESET) se reintenta 3 veces y falla en el 4to -> TranscriptFailed(UploadFailed)', async () => {
    const event = makeEvent();
    const uploadFile = vi.fn(() => Promise.reject(Object.assign(new Error('socket hang up'), { code: 'ECONNRESET' })));
    const cloudStorage = makeCloudStorage({ uploadFile });

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow('socket hang up');

    expect(uploadFile).toHaveBeenCalledTimes(4);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({ failureReason: 'UploadFailed', errorMessage: 'socket hang up' }),
    );
  });

  it('upload con AccessDenied (no retriable) -> un solo intento, falla de inmediato', async () => {
    const event = makeEvent();
    const uploadFile = vi.fn(() => Promise.reject(Object.assign(new Error('access denied'), { code: 'AccessDenied' })));
    const cloudStorage = makeCloudStorage({ uploadFile });

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow('access denied');

    expect(uploadFile).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({ failureReason: 'UploadFailed' }),
    );
  });
});
