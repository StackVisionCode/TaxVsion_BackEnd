import { describe, it, expect, vi, beforeEach } from 'vitest';
import { randomUUID } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import type { CloudStorageClient } from '../../src/cloudstorage/cloudstorage-client.js';
import type { RecordingReadyEvent } from '../../src/rabbit/consumer.js';
import { NoAudioStreamError, ProcessTimeoutError } from '../../src/errors.js';
import {
  pipelineFailuresTotal,
  realtimeFactor,
  tooLongTotal,
  wordsPerSecondAnomaliesTotal,
} from '../../src/telemetry/metrics.js';

/**
 * F7 (T-28/D-27/INV-12 H-1) — pipeline con pistas por participante, salida
 * inline de notas de voz, TooLong y timeouts. download/ffmpeg/whisper/upload
 * mockeados; el contenido subido se lee del disco en el momento del upload.
 */

const mocks = vi.hoisted(() => ({
  publishTranscriptReady: vi.fn(),
  publishTranscriptFailed: vi.fn(),
  transcodeToWav16kMono: vi.fn(),
  probeMedia: vi.fn(),
  probeDurationSeconds: vi.fn(),
  transcribeWav: vi.fn(),
}));

vi.mock('../../src/rabbit/publisher.js', () => ({
  publishTranscriptReady: mocks.publishTranscriptReady,
  publishTranscriptFailed: mocks.publishTranscriptFailed,
}));
vi.mock('../../src/media/audio-transcoder.js', () => ({
  transcodeToWav16kMono: mocks.transcodeToWav16kMono,
  probeMedia: mocks.probeMedia,
  probeDurationSeconds: mocks.probeDurationSeconds,
}));
vi.mock('../../src/whisper/whisper-transcriber.js', () => ({
  transcribeWav: mocks.transcribeWav,
}));

const { processRecordingReady } = await import('../../src/pipeline.js');

interface Upload {
  originalName: string;
  contentType: string;
  ownerId: string | null;
  content: string;
}

function makeCloudStorage(): { client: CloudStorageClient; downloads: string[]; uploads: Upload[] } {
  const downloads: string[] = [];
  const uploads: Upload[] = [];
  const client = {
    downloadFile: vi.fn(async (_tenantId: string, fileId: string) => {
      downloads.push(fileId);
    }),
    uploadFile: vi.fn(
      async (input: { filePath: string; originalName: string; contentType: string; ownerId: string | null }) => {
        uploads.push({
          originalName: input.originalName,
          contentType: input.contentType,
          ownerId: input.ownerId,
          content: await readFile(input.filePath, 'utf-8'),
        });
        return { fileId: `file-${uploads.length}` };
      },
    ),
  } as unknown as CloudStorageClient;
  return { client, downloads, uploads };
}

function meetingEvent(overrides: Partial<RecordingReadyEvent> = {}): RecordingReadyEvent {
  return {
    kind: 'meeting',
    eventId: randomUUID(),
    tenantId: randomUUID(),
    correlationId: randomUUID(),
    targetId: 'meeting-1',
    recordingFileId: 'composed-file',
    output: 'file',
    passthrough: {},
    audioTracks: [
      { fileId: 'track-ana', userId: 'u-ana', displayName: 'Ana Garcia', startOffsetMs: 0 },
      { fileId: 'track-luis', userId: 'u-luis', displayName: 'Luis Perez', startOffsetMs: 5_000 },
    ],
    language: 'es',
    prompt: 'IRS, 1040',
    maxDurationSeconds: 3600,
    timeoutFactorPct: 150,
    ...overrides,
  };
}

const SEGMENTS_BY_SOURCE: Record<string, { startMs: number; endMs: number; text: string }[]> = {
  'source-0': [
    { startMs: 0, endMs: 3_000, text: 'Buenos dias.' },
    { startMs: 9_000, endMs: 11_000, text: 'Perfecto, seguimos.' },
  ],
  'source-1': [{ startMs: 0, endMs: 2_500, text: 'Hola, ya estoy.' }],
};

function sourceOf(filePath: string): string {
  return path.basename(filePath).replace(/\.(wav|bin)$/, '');
}

function metricValue(values: { labels: Record<string, unknown>; value: number }[], labels: Record<string, string>) {
  return values.find((v) => Object.entries(labels).every(([k, val]) => v.labels[k] === val))?.value;
}

beforeEach(() => {
  vi.clearAllMocks();
  pipelineFailuresTotal.reset();
  tooLongTotal.reset();
  wordsPerSecondAnomaliesTotal.reset();
  realtimeFactor.reset();
  mocks.probeMedia.mockImplementation(async (filePath: string) => ({
    audioStreamCount: 1,
    durationSeconds: sourceOf(filePath) === 'source-0' ? 600 : 300,
  }));
  mocks.probeDurationSeconds.mockResolvedValue(600);
  mocks.transcodeToWav16kMono.mockResolvedValue(undefined);
  mocks.transcribeWav.mockImplementation(async (wavPath: string) => ({
    segments: SEGMENTS_BY_SOURCE[sourceOf(wavPath)] ?? [],
    detectedLanguage: 'es',
    processingSeconds: sourceOf(wavPath) === 'source-0' ? 2 : 1,
  }));
});

describe('processRecordingReady — pistas por participante (T-28)', () => {
  it('cada pista se transcribe en su idioma; sin idioma propio usa el del evento (INV-T01)', async () => {
    const { client } = makeCloudStorage();
    const event = meetingEvent({
      language: 'auto',
      audioTracks: [
        { fileId: 'track-ana', userId: 'u-ana', displayName: 'Ana Garcia', startOffsetMs: 0, language: 'es' },
        { fileId: 'track-luis', userId: 'u-luis', displayName: 'Luis Perez', startOffsetMs: 5_000 },
      ],
    });

    await processRecordingReady(event, { cloudStorage: client });

    const languages = mocks.transcribeWav.mock.calls.map((call) => (call[2] as { language: string }).language);
    expect(languages).toEqual(['es', 'auto']);
  });

  it('descarga cada pista (no el compuesto), entrelaza por tiempo y sube TXT + JSON v2', async () => {
    const { client, downloads, uploads } = makeCloudStorage();
    const event = meetingEvent();

    await processRecordingReady(event, { cloudStorage: client });

    expect(downloads).toEqual(['track-ana', 'track-luis']);

    // Timeout por corrida = max(600, 3600 x 150 %) = 5400 s, en ffmpeg y whisper.
    expect(mocks.transcodeToWav16kMono).toHaveBeenCalledWith(expect.any(String), expect.any(String), 5_400_000);
    expect(mocks.transcribeWav).toHaveBeenCalledWith(expect.any(String), expect.any(String), {
      language: 'es',
      prompt: 'IRS, 1040',
      timeoutMs: 5_400_000,
    });

    expect(uploads.map((u) => [u.contentType, u.ownerId])).toEqual([
      ['text/plain', 'meeting-1'],
      ['application/json', 'meeting-1'],
    ]);
    expect(uploads[0]?.originalName).toMatch(/^Meeting_Transcript_\d{4}-\d{2}-\d{2}\.txt$/);
    expect(uploads[1]?.originalName).toMatch(/^Meeting_Transcript_\d{4}-\d{2}-\d{2}\.json$/);
    expect(uploads[0]?.content).toBe(
      '[00:00:00] Ana Garcia: Buenos dias.\n[00:00:05] Luis Perez: Hola, ya estoy.\n[00:00:09] Ana Garcia: Perfecto, seguimos.\n',
    );

    const json = JSON.parse(uploads[1]?.content ?? '{}') as Record<string, unknown>;
    expect(json).toMatchObject({
      version: 2,
      language: 'es',
      model: 'ggml-small-q5_1.bin',
      vad: false,
      durationSeconds: 600,
      speakers: [
        { userId: 'u-ana', displayName: 'Ana Garcia' },
        { userId: 'u-luis', displayName: 'Luis Perez' },
      ],
    });
    expect(json['segments']).toEqual([
      { startMs: 0, endMs: 3_000, speakerUserId: 'u-ana', speakerName: 'Ana Garcia', text: 'Buenos dias.' },
      { startMs: 5_000, endMs: 7_500, speakerUserId: 'u-luis', speakerName: 'Luis Perez', text: 'Hola, ya estoy.' },
      { startMs: 9_000, endMs: 11_000, speakerUserId: 'u-ana', speakerName: 'Ana Garcia', text: 'Perfecto, seguimos.' },
    ]);

    expect(mocks.publishTranscriptReady).toHaveBeenCalledTimes(1);
    expect(mocks.publishTranscriptReady).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: 'meeting',
        targetId: 'meeting-1',
        recordingFileId: 'composed-file',
        detectedLanguage: 'es',
        durationSeconds: 600,
        wordCount: 7,
        model: 'ggml-small-q5_1.bin',
        vad: false,
        // (2 + 1) s de whisper / (600 + 300) s de audio
        realtimeFactor: 0.003,
        wordsPerSecond: 0.012,
        output: {
          type: 'file',
          transcriptFileId: 'file-1',
          transcriptJsonFileId: 'file-2',
          segmentCount: 3,
          speakerCount: 2,
        },
      }),
    );
    expect(mocks.publishTranscriptFailed).not.toHaveBeenCalled();

    const rtf = await realtimeFactor.get();
    expect(
      rtf.values.find(
        (v) => v.metricName === 'transcript_worker_realtime_factor_count' && v.labels.model === 'ggml-small-q5_1.bin',
      )?.value,
    ).toBe(1);
    // 7 palabras en 600 s => por debajo de WPS_MIN (0,3)
    const anomalies = await wordsPerSecondAnomaliesTotal.get();
    expect(metricValue(anomalies.values, { kind: 'meeting', direction: 'low' })).toBe(1);
  });

  it('una pista sin audio se omite; el resto se transcribe', async () => {
    mocks.probeMedia.mockImplementation(async (filePath: string) => {
      if (sourceOf(filePath) === 'source-1') throw new NoAudioStreamError('0 audio streams');
      return { audioStreamCount: 1, durationSeconds: 600 };
    });
    const { client, uploads } = makeCloudStorage();

    await processRecordingReady(meetingEvent(), { cloudStorage: client });

    expect(mocks.transcribeWav).toHaveBeenCalledTimes(1);
    expect(uploads[0]?.content).not.toContain('Luis');
    expect(mocks.publishTranscriptReady).toHaveBeenCalledWith(
      expect.objectContaining({ output: expect.objectContaining({ speakerCount: 1, segmentCount: 2 }) }),
    );
    expect(mocks.publishTranscriptFailed).not.toHaveBeenCalled();
  });

  it('ninguna pista con audio => TranscriptFailed(NoAudioStream), sin subida', async () => {
    mocks.probeMedia.mockRejectedValue(new NoAudioStreamError('0 audio streams'));
    const { client, uploads } = makeCloudStorage();

    await expect(processRecordingReady(meetingEvent(), { cloudStorage: client })).rejects.toThrow('0 audio streams');

    expect(uploads).toEqual([]);
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({ failureReason: 'NoAudioStream', targetId: 'meeting-1' }),
    );
  });
});

describe('processRecordingReady — limites (INV-12 H-1)', () => {
  it('ffprobe > maxDuration + tolerancia => TooLong antes del transcode', async () => {
    mocks.probeMedia.mockResolvedValue({ audioStreamCount: 1, durationSeconds: 3_800 });
    const { client } = makeCloudStorage();

    await expect(processRecordingReady(meetingEvent(), { cloudStorage: client })).rejects.toThrow(/maxDurationSeconds/);

    expect(mocks.transcodeToWav16kMono).not.toHaveBeenCalled();
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(expect.objectContaining({ failureReason: 'TooLong' }));
    expect(metricValue((await tooLongTotal.get()).values, { kind: 'meeting' })).toBe(1);
    expect(metricValue((await pipelineFailuresTotal.get()).values, { reason: 'TooLong', kind: 'meeting' })).toBe(1);
  });

  it('el offset de la pista cuenta: 3000 s + 800 s > 3600 + 120', async () => {
    mocks.probeMedia.mockResolvedValue({ audioStreamCount: 1, durationSeconds: 800 });
    const { client } = makeCloudStorage();
    const event = meetingEvent({
      audioTracks: [{ fileId: 'late', userId: 'u-late', displayName: 'Tarde', startOffsetMs: 3_000_000 }],
    });

    await expect(processRecordingReady(event, { cloudStorage: client })).rejects.toThrow();

    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(expect.objectContaining({ failureReason: 'TooLong' }));
  });

  it('original sin duracion (WebM de MediaRecorder): se mide el WAV y se aplica TooLong antes de whisper', async () => {
    mocks.probeMedia.mockResolvedValue({ audioStreamCount: 1, durationSeconds: null });
    mocks.probeDurationSeconds.mockResolvedValue(4_000);
    const { client } = makeCloudStorage();

    await expect(
      processRecordingReady(meetingEvent({ audioTracks: undefined }), { cloudStorage: client }),
    ).rejects.toThrow();

    expect(mocks.transcodeToWav16kMono).toHaveBeenCalledTimes(1);
    expect(mocks.transcribeWav).not.toHaveBeenCalled();
    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(expect.objectContaining({ failureReason: 'TooLong' }));
  });

  it('sin maxDurationSeconds en el evento no hay TooLong y el timeout es el minimo (600 s)', async () => {
    mocks.probeMedia.mockResolvedValue({ audioStreamCount: 1, durationSeconds: 99_999 });
    const { client } = makeCloudStorage();

    await processRecordingReady(
      meetingEvent({ audioTracks: undefined, maxDurationSeconds: undefined, timeoutFactorPct: undefined }),
      { cloudStorage: client },
    );

    expect(mocks.transcribeWav).toHaveBeenCalledWith(
      expect.any(String),
      expect.any(String),
      expect.objectContaining({ timeoutMs: 600_000 }),
    );
    expect(mocks.publishTranscriptFailed).not.toHaveBeenCalled();
  });

  it('whisper vencido => TranscriptFailed(Timeout)', async () => {
    mocks.transcribeWav.mockRejectedValue(new ProcessTimeoutError('whisper-cli exceeded 5400000 ms'));
    const { client } = makeCloudStorage();

    await expect(processRecordingReady(meetingEvent(), { cloudStorage: client })).rejects.toThrow('exceeded');

    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(expect.objectContaining({ failureReason: 'Timeout' }));
  });

  it('ffmpeg vencido => TranscriptFailed(Timeout); otro error de ffmpeg => FfmpegError', async () => {
    const { client } = makeCloudStorage();
    mocks.transcodeToWav16kMono.mockRejectedValueOnce(new ProcessTimeoutError('ffmpeg exceeded'));
    await expect(processRecordingReady(meetingEvent(), { cloudStorage: client })).rejects.toThrow();
    expect(mocks.publishTranscriptFailed).toHaveBeenLastCalledWith(
      expect.objectContaining({ failureReason: 'Timeout' }),
    );

    mocks.transcodeToWav16kMono.mockRejectedValueOnce(new Error('ffmpeg exited with code 1'));
    await expect(processRecordingReady(meetingEvent(), { cloudStorage: client })).rejects.toThrow();
    expect(mocks.publishTranscriptFailed).toHaveBeenLastCalledWith(
      expect.objectContaining({ failureReason: 'FfmpegError' }),
    );
  });
});

describe('processRecordingReady — archivo unico (sin pistas)', () => {
  it('TXT con [hh:mm:ss] texto y JSON v2 sin hablantes; idioma del env si el evento no lo trae', async () => {
    mocks.probeMedia.mockResolvedValue({ audioStreamCount: 1, durationSeconds: 12 });
    mocks.transcribeWav.mockResolvedValue({
      segments: [{ startMs: 1_000, endMs: 4_000, text: 'hola mundo' }],
      detectedLanguage: 'es',
      processingSeconds: 3,
    });
    const { client, downloads, uploads } = makeCloudStorage();
    const event = meetingEvent({
      kind: 'call',
      targetId: 'call-1',
      audioTracks: undefined,
      language: undefined,
      prompt: undefined,
    });

    await processRecordingReady(event, { cloudStorage: client });

    expect(downloads).toEqual(['composed-file']);
    expect(mocks.transcribeWav).toHaveBeenCalledWith(expect.any(String), expect.any(String), {
      language: 'auto',
      prompt: null,
      timeoutMs: 5_400_000,
    });
    expect(uploads[0]?.content).toBe('[00:00:01] hola mundo\n');
    const json = JSON.parse(uploads[1]?.content ?? '{}') as { speakers: unknown[]; segments: { speakerUserId: unknown }[] };
    expect(json.speakers).toEqual([]);
    expect(json.segments[0]?.speakerUserId).toBeNull();
    expect(mocks.publishTranscriptReady).toHaveBeenCalledWith(
      expect.objectContaining({
        durationSeconds: 12,
        realtimeFactor: 0.25,
        output: expect.objectContaining({ speakerCount: 0, segmentCount: 1 }),
      }),
    );
  });
});

describe('processRecordingReady — nota de voz inline (D-27)', () => {
  function voiceNoteEvent(): RecordingReadyEvent {
    return {
      kind: 'voice-note',
      eventId: randomUUID(),
      tenantId: randomUUID(),
      correlationId: undefined,
      targetId: 'msg-1',
      recordingFileId: 'voice-file',
      output: 'inline',
      passthrough: { conversationId: 'conv-1' },
      language: 'es',
      prompt: null,
    };
  }

  it('no sube nada y publica transcriptText con messageId/conversationId', async () => {
    mocks.probeMedia.mockResolvedValue({ audioStreamCount: 1, durationSeconds: 20 });
    mocks.transcribeWav.mockResolvedValue({
      segments: [
        { startMs: 0, endMs: 2_000, text: 'Hola,' },
        { startMs: 2_000, endMs: 4_000, text: 'te llamo luego.' },
      ],
      detectedLanguage: 'es',
      processingSeconds: 2,
    });
    const { client, downloads, uploads } = makeCloudStorage();

    await processRecordingReady(voiceNoteEvent(), { cloudStorage: client });

    expect(downloads).toEqual(['voice-file']);
    expect(uploads).toEqual([]);
    expect(mocks.publishTranscriptReady).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: 'voice-note',
        targetId: 'msg-1',
        recordingFileId: 'voice-file',
        passthrough: { conversationId: 'conv-1' },
        detectedLanguage: 'es',
        durationSeconds: 20,
        model: 'ggml-small-q5_1.bin',
        output: { type: 'inline', transcriptText: 'Hola, te llamo luego.' },
      }),
    );
  });

  it('recorta transcriptText a TRANSCRIPT_WORKER_INLINE_TEXT_MAX_CHARS (4000)', async () => {
    mocks.probeMedia.mockResolvedValue({ audioStreamCount: 1, durationSeconds: 600 });
    mocks.transcribeWav.mockResolvedValue({
      segments: [{ startMs: 0, endMs: 600_000, text: 'palabra '.repeat(1000) }],
      detectedLanguage: 'es',
      processingSeconds: 10,
    });
    const { client } = makeCloudStorage();

    await processRecordingReady(voiceNoteEvent(), { cloudStorage: client });

    const published = mocks.publishTranscriptReady.mock.calls[0]?.[0] as { output: { transcriptText: string } };
    expect(published.output.transcriptText.length).toBeLessThanOrEqual(4000);
    expect(published.output.transcriptText.startsWith('palabra palabra')).toBe(true);
  });

  it('falla con passthrough en TranscriptFailed (conversationId de vuelta)', async () => {
    mocks.probeMedia.mockRejectedValue(new NoAudioStreamError('0 audio streams'));
    const { client } = makeCloudStorage();

    await expect(processRecordingReady(voiceNoteEvent(), { cloudStorage: client })).rejects.toThrow();

    expect(mocks.publishTranscriptFailed).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: 'voice-note',
        targetId: 'msg-1',
        passthrough: { conversationId: 'conv-1' },
        failureReason: 'NoAudioStream',
      }),
    );
  });
});
