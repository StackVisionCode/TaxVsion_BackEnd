import { describe, it, expect, vi, beforeEach } from 'vitest';
import { randomUUID } from 'node:crypto';
import type { CloudStorageClient } from '../../src/cloudstorage/cloudstorage-client.js';
import type { RecordingReadyEvent } from '../../src/rabbit/consumer.js';

/**
 * Fase Transcript 2 + 3 — cada test simula la falla de UN stage del pipeline
 * (audioProbe/download/transcode/transcribe/uploadTranscript/publishReady) y
 * verifica:
 *   1. `publishTranscriptFailed` se llama con el `failureReason` correcto
 *      para ese stage (mapeo definido en pipeline.ts).
 *   2. El error original sigue propagando (processRecordingReady rechaza) —
 *      asi consumer.ts sigue haciendo su log + inbox.unmark + nack existente,
 *      sin que este cambio silencie el error ni agregue un retry in-worker.
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
  // Default happy-path stubs — cada test overridea solo el stage que quiere
  // hacer fallar; los stages anteriores deben "pasar" para llegar hasta ahi.
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

describe('processRecordingReady — TranscriptFailed por stage (Fase Transcript 2/3)', () => {
  it('no audio stream -> publishTranscriptFailed(NoAudioStream) + rethrow (nack, no llega a whisper)', async () => {
    const event = makeEvent();
    const noAudioErr = new Error('ffprobe reported 0 audio streams for recording.bin');
    mocks.probeAudioStreams.mockRejectedValueOnce(noAudioErr);
    const cloudStorage = makeCloudStorage({});

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow(noAudioErr);

    expect(mocks.publishTranscriptFailed).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: 'meeting',
        tenantId: event.tenantId,
        targetId: event.targetId,
        recordingFileId: event.recordingFileId,
        failureReason: 'NoAudioStream',
        errorMessage: noAudioErr.message,
      }),
    );
    // No llega a transcode/whisper.
    expect(mocks.transcodeToWav16kMono).not.toHaveBeenCalled();
    expect(mocks.transcribeWav).not.toHaveBeenCalled();
  });

  it('download failure (non-retriable: plain Error, no status) -> single attempt, publishTranscriptFailed(DownloadFailed) + rethrow', async () => {
    const event = makeEvent();
    const downloadErr = new Error('download-url request failed with status 500');
    const downloadFile = vi.fn(() => Promise.reject(downloadErr));
    const cloudStorage = makeCloudStorage({ downloadFile });

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow(downloadErr);

    // Fase Transcript 4 — un Error plano (sin DownloadStatusError.status) no
    // es retriable por diseño: un solo intento, sin backoff de por medio.
    expect(downloadFile).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: 'meeting',
        tenantId: event.tenantId,
        targetId: event.targetId,
        recordingFileId: event.recordingFileId,
        failureReason: 'DownloadFailed',
        errorMessage: downloadErr.message,
      }),
    );
  });

  it('transcode (ffmpeg) failure -> single attempt (no retry), publishTranscriptFailed(FfmpegError) + rethrow', async () => {
    const event = makeEvent({ kind: 'call' });
    const ffmpegErr = new Error('ffmpeg exited with code 1');
    mocks.transcodeToWav16kMono.mockRejectedValueOnce(ffmpegErr);
    const cloudStorage = makeCloudStorage({});

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow(ffmpegErr);

    // Fase Transcript 4 — ffmpeg NUNCA reintenta (falla deterministica).
    expect(mocks.transcodeToWav16kMono).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'call', failureReason: 'FfmpegError', errorMessage: ffmpegErr.message }),
    );
  });

  it('transcribe (whisper) failure -> single attempt (no retry), publishTranscriptFailed(WhisperError) + rethrow', async () => {
    const event = makeEvent();
    const whisperErr = new Error('whisper-cli exited with code 1');
    mocks.transcribeWav.mockRejectedValueOnce(whisperErr);
    const cloudStorage = makeCloudStorage({});

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow(whisperErr);

    // Fase Transcript 4 — whisper NUNCA reintenta (falla deterministica).
    expect(mocks.transcribeWav).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({ failureReason: 'WhisperError', errorMessage: whisperErr.message }),
    );
  });

  it('uploadTranscript failure (non-retriable: plain Error, no .code) -> single attempt, publishTranscriptFailed(UploadFailed) + rethrow', async () => {
    const event = makeEvent();
    const uploadErr = new Error('MinIO putObject failed');
    const uploadFile = vi.fn(() => Promise.reject(uploadErr));
    const cloudStorage = makeCloudStorage({ uploadFile });

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow(uploadErr);

    expect(uploadFile).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({ failureReason: 'UploadFailed', errorMessage: uploadErr.message }),
    );
  });

  it('publishReady failure -> publishTranscriptFailed(Timeout) + rethrow', async () => {
    const event = makeEvent();
    const publishErr = new Error('channel closed');
    mocks.publishTranscriptReady.mockImplementationOnce(() => {
      throw publishErr;
    });
    const cloudStorage = makeCloudStorage({});

    await expect(processRecordingReady(event, { cloudStorage })).rejects.toThrow(publishErr);

    expect(mocks.publishTranscriptFailed).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({ failureReason: 'Timeout', errorMessage: publishErr.message }),
    );
  });

  it('happy path never calls publishTranscriptFailed', async () => {
    const event = makeEvent();
    const cloudStorage = makeCloudStorage({});

    await processRecordingReady(event, { cloudStorage });

    expect(mocks.publishTranscriptFailed).not.toHaveBeenCalled();
    expect(mocks.publishTranscriptReady).toHaveBeenCalledTimes(1);
  });
});
