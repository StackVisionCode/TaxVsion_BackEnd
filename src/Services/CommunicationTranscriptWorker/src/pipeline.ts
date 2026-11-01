import { randomUUID } from 'node:crypto';
import { mkdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { CloudStorageClient } from './cloudstorage/cloudstorage-client.js';
import { DownloadStatusError } from './cloudstorage/cloudstorage-client.js';
import type { AudioTrackRef, RecordingReadyEvent } from './rabbit/consumer.js';
import { publishTranscriptReady, publishTranscriptFailed } from './rabbit/publisher.js';
import { transcodeToWav16kMono, probeMedia, probeDurationSeconds } from './media/audio-transcoder.js';
import { transcribeWav } from './whisper/whisper-transcriber.js';
import { resolveWhisperLanguage } from './whisper/whisper-args.js';
import { config } from './config.js';
import { logger } from './logger.js';
import type { TranscriptFailureReason } from './contracts/events.js';
import { NoAudioStreamError, ProcessTimeoutError, TooLongError } from './errors.js';
import { withRetry, type RetryPolicy } from './retry.js';
import {
  pipelineDurationSeconds,
  pipelineFailuresTotal,
  realtimeFactor,
  tooLongTotal,
  wordsPerSecondAnomaliesTotal,
} from './telemetry/metrics.js';
import {
  buildTranscriptJsonV2,
  collectSpeakers,
  countWords,
  formatTranscriptTxt,
  mergeSegments,
  pickDominantLanguage,
  plainText,
  shiftAndLabel,
  truncateInlineText,
  type TrackLabel,
  type TranscriptSegment,
} from './transcript/transcript-format.js';
import { classifyWordsPerSecond, computeRatio, deriveRunTimeoutMs, isTooLong } from './transcript/limits.js';

// Solo download y upload reintentan (fallas transientes); ffmpeg/whisper nunca
// (deterministicas). maxAttempts = intento original + reintentos.
const RETRY_MAX_ATTEMPTS = config.retry.maxAttempts;
const RETRY_BACKOFF_MS = config.retry.backoffMs;
const NOT_AVAILABLE_POLICY: RetryPolicy = config.retry.notAvailable;

// File.NotAvailable = archivo aun en escaneo ClamAV (CloudStorage escanea de a
// uno: con varias pistas tarda minutos) => politica de espera larga propia.
// Otros 403 (File.Forbidden) no se reintentan: un retry no los arregla.
function isNotAvailable(err: unknown): boolean {
  return err instanceof DownloadStatusError && err.errorCode === 'File.NotAvailable';
}

function isRetriableDownloadError(err: unknown): boolean {
  if (!(err instanceof DownloadStatusError)) return false;
  return err.status >= 500 || isNotAvailable(err);
}

// Blips de red/5xx hacia MinIO. Credenciales o policy mal configuradas no
// estan a proposito: reintentarlas solo demora el nack.
const RETRIABLE_UPLOAD_CODES = new Set([
  'ECONNRESET',
  'ETIMEDOUT',
  'ECONNREFUSED',
  'EPIPE',
  'EAI_AGAIN',
  'InternalError',
  'SlowDown',
  'RequestTimeout',
  'ServiceUnavailable',
]);

function isRetriableUploadError(err: unknown): boolean {
  const code = (err as { code?: unknown } | null)?.code;
  return typeof code === 'string' && RETRIABLE_UPLOAD_CODES.has(code);
}

/** Fallo de un stage con su reason; el catch de arriba reporta y relanza la causa original. */
class StageFailure extends Error {
  constructor(
    readonly reason: TranscriptFailureReason,
    readonly original: Error,
  ) {
    super(original.message);
    this.name = 'StageFailure';
  }
}

function reasonFor(defaultReason: TranscriptFailureReason, err: unknown): TranscriptFailureReason {
  if (err instanceof ProcessTimeoutError) return 'Timeout';
  if (err instanceof TooLongError) return 'TooLong';
  return defaultReason;
}

async function stage<T>(reason: TranscriptFailureReason, fn: () => Promise<T>): Promise<T> {
  try {
    return await fn();
  } catch (err) {
    if (err instanceof StageFailure) throw err;
    const error = err instanceof Error ? err : new Error(String(err));
    throw new StageFailure(reasonFor(reason, error), error);
  }
}

interface AudioSource {
  readonly fileId: string;
  readonly label: TrackLabel | null;
  readonly startOffsetMs: number;
  /** Idioma de esta fuente; null = el del evento (INV-T01). */
  readonly language?: string | null;
}

interface SourceResult {
  readonly segments: TranscriptSegment[];
  /** Duracion propia del audio de la fuente. */
  readonly durationSeconds: number;
  /** Fin en la linea de tiempo de la grabacion (offset + duracion). */
  readonly endSeconds: number;
  readonly detectedLanguage: string | null;
  readonly processingSeconds: number;
}

interface RunContext {
  readonly event: RecordingReadyEvent;
  readonly workDir: string;
  readonly timeoutMs: number;
  readonly language: string;
  readonly prompt: string | null;
  readonly cloudStorage: CloudStorageClient;
}

function toSources(event: RecordingReadyEvent): AudioSource[] {
  const tracks: readonly AudioTrackRef[] = event.audioTracks ?? [];
  if (tracks.length === 0) return [{ fileId: event.recordingFileId, label: null, startOffsetMs: 0 }];
  return tracks.map((track) => ({
    fileId: track.fileId,
    label: { userId: track.userId, displayName: track.displayName },
    startOffsetMs: track.startOffsetMs,
    language: track.language ?? null,
  }));
}

function assertNotTooLong(ctx: RunContext, source: AudioSource, durationSeconds: number | null): void {
  if (durationSeconds === null) return;
  const endSeconds = source.startOffsetMs / 1000 + durationSeconds;
  if (isTooLong(endSeconds, ctx.event.maxDurationSeconds, config.limits.durationToleranceSeconds)) {
    throw new TooLongError(
      `audio ends at ${Math.round(endSeconds)} s, above maxDurationSeconds ${ctx.event.maxDurationSeconds} + tolerance ${config.limits.durationToleranceSeconds} s`,
    );
  }
}

/**
 * download → ffprobe (audio + duracion + TooLong) → ffmpeg → whisper de UNA
 * fuente (archivo unico o pista). Con pistas, una pista sin audio se omite
 * (`skipOnNoAudio`); sin pistas, es NoAudioStream como siempre.
 */
async function transcribeSource(
  ctx: RunContext,
  source: AudioSource,
  index: number,
  skipOnNoAudio: boolean,
): Promise<SourceResult | NoAudioStreamError> {
  const { event, workDir } = ctx;
  const base = path.join(workDir, `source-${index}`);
  const originalPath = `${base}.bin`;
  const wavPath = `${base}.wav`;

  try {
    logger.info({ eventId: event.eventId, fileId: source.fileId, index }, 'downloading audio');
    await stage('DownloadFailed', () =>
      withRetry(() => ctx.cloudStorage.downloadFile(event.tenantId, source.fileId, originalPath), {
        maxAttempts: RETRY_MAX_ATTEMPTS,
        backoffMs: RETRY_BACKOFF_MS,
        isRetriable: isRetriableDownloadError,
        policyFor: (err) => (isNotAvailable(err) ? NOT_AVAILABLE_POLICY : undefined),
        onRetry: (attempt, err, delayMs) =>
          logger.warn(
            { eventId: event.eventId, fileId: source.fileId, attempt, err: (err as Error).message, delayMs },
            'download failed (retriable); retrying',
          ),
      }),
    );

    let probedSeconds: number | null;
    try {
      ({ durationSeconds: probedSeconds } = await probeMedia(originalPath));
    } catch (err) {
      const error = err instanceof Error ? err : new Error(String(err));
      if (!skipOnNoAudio) throw new StageFailure('NoAudioStream', error);
      logger.warn({ eventId: event.eventId, fileId: source.fileId, err: error.message }, 'audio track without audio; skipped');
      return error instanceof NoAudioStreamError ? error : new NoAudioStreamError(error.message);
    }
    await stage('TooLong', async () => assertNotTooLong(ctx, source, probedSeconds));

    logger.info({ eventId: event.eventId, index }, 'transcoding to wav');
    await stage('FfmpegError', () => transcodeToWav16kMono(originalPath, wavPath, ctx.timeoutMs));
    await rm(originalPath, { force: true });

    let durationSeconds = probedSeconds;
    if (durationSeconds === null) {
      durationSeconds = await stage('FfmpegError', () => probeDurationSeconds(wavPath));
      await stage('TooLong', async () => assertNotTooLong(ctx, source, durationSeconds));
    }

    logger.info({ eventId: event.eventId, index }, 'running whisper.cpp');
    const transcription = await stage('WhisperError', () =>
      transcribeWav(wavPath, base, { language: source.language ?? ctx.language, prompt: ctx.prompt, timeoutMs: ctx.timeoutMs }),
    );

    const lastEndMs = transcription.segments.reduce((max, segment) => Math.max(max, segment.endMs), 0);
    const ownSeconds = durationSeconds ?? lastEndMs / 1000;
    return {
      segments: shiftAndLabel(transcription.segments, source.startOffsetMs, source.label),
      durationSeconds: ownSeconds,
      endSeconds: source.startOffsetMs / 1000 + ownSeconds,
      detectedLanguage: transcription.detectedLanguage,
      processingSeconds: transcription.processingSeconds,
    };
  } finally {
    // Borrado por fuente: con N pistas no se acumulan N WAV en disco.
    await Promise.all([rm(originalPath, { force: true }), rm(wavPath, { force: true })]);
  }
}

function downloadName(kind: string, extension: 'txt' | 'json'): string {
  const capKind = kind.charAt(0).toUpperCase() + kind.slice(1);
  return `${capKind}_Transcript_${new Date().toISOString().slice(0, 10)}.${extension}`;
}

async function uploadTranscriptFile(
  ctx: RunContext,
  content: string,
  extension: 'txt' | 'json',
  contentType: string,
): Promise<string> {
  const { event, workDir } = ctx;
  const localPath = path.join(workDir, `${event.kind}-${event.targetId}-transcript-${randomUUID()}.${extension}`);
  await writeFile(localPath, content, 'utf-8');
  const uploaded = await withRetry(
    () =>
      ctx.cloudStorage.uploadFile({
        tenantId: event.tenantId,
        filePath: localPath,
        originalName: downloadName(event.kind, extension),
        contentType,
        sizeBytes: Buffer.byteLength(content, 'utf-8'),
        ownerId: event.targetId,
        correlationId: event.correlationId,
      }),
    {
      maxAttempts: RETRY_MAX_ATTEMPTS,
      backoffMs: RETRY_BACKOFF_MS,
      isRetriable: isRetriableUploadError,
      onRetry: (attempt, err, delayMs) =>
        logger.warn(
          { eventId: event.eventId, extension, attempt, err: (err as Error).message, delayMs },
          'upload failed (retriable); retrying',
        ),
    },
  );
  return uploaded.fileId;
}

/**
 * Orquesta un trigger de transcripcion (contratos 9.3/9.4):
 *  - Con `audioTracks` (meetings/calls server-side, T-28): transcribe cada
 *    pista, desplaza por `startOffsetMs`, etiqueta con el nombre y entrelaza.
 *  - Sin pistas: archivo unico (`recordingFileId`), segmentos sin hablante.
 *  - Salida `file`: sube TXT (`[hh:mm:ss] Nombre: texto`) + JSON v2 a
 *    `Transcripts` y publica transcript_ready v2.
 *  - Salida `inline` (notas de voz, D-27): sin subida, el texto va en el evento.
 *
 * Cada stage que falla publica TranscriptFailed con su reason y relanza el
 * error original (consumer.ts: unmark + nack a la DLQ). Temporales en
 * `tempDir/{eventId}`, borrados siempre.
 */
export async function processRecordingReady(
  event: RecordingReadyEvent,
  deps: { cloudStorage: CloudStorageClient },
): Promise<void> {
  const workDir = path.join(config.whisper.tempDir, event.eventId);
  await mkdir(workDir, { recursive: true });

  const stopTimer = pipelineDurationSeconds.startTimer({ kind: event.kind });
  let status: 'success' | 'failure' = 'success';

  try {
    const ctx: RunContext = {
      event,
      workDir,
      cloudStorage: deps.cloudStorage,
      timeoutMs: deriveRunTimeoutMs({
        minTimeoutSeconds: config.limits.minTimeoutSeconds,
        maxDurationSeconds: event.maxDurationSeconds,
        timeoutFactorPct: event.timeoutFactorPct,
      }),
      language: resolveWhisperLanguage(event.language, config.whisper.language),
      prompt: event.prompt?.trim() || config.whisper.prompt,
    };

    const sources = toSources(event);
    const multiTrack = (event.audioTracks?.length ?? 0) > 0;
    const results: SourceResult[] = [];
    let lastSkip: NoAudioStreamError | undefined;
    for (const [index, source] of sources.entries()) {
      const outcome = await transcribeSource(ctx, source, index, multiTrack);
      if (outcome instanceof NoAudioStreamError) lastSkip = outcome;
      else results.push(outcome);
    }
    if (results.length === 0) {
      throw new StageFailure('NoAudioStream', lastSkip ?? new NoAudioStreamError('no audio track could be read'));
    }

    const segments = mergeSegments(results.map((r) => r.segments));
    const durationSeconds = Math.round(Math.max(...results.map((r) => r.endSeconds)));
    const audioSeconds = results.reduce((sum, r) => sum + r.durationSeconds, 0);
    const whisperSeconds = results.reduce((sum, r) => sum + r.processingSeconds, 0);
    const detectedLanguage = pickDominantLanguage(results.map((r) => r.detectedLanguage));
    const text = plainText(segments);
    const wordCount = countWords(text);
    const wordsPerSecond = computeRatio(wordCount, durationSeconds);
    const rtf = computeRatio(whisperSeconds, audioSeconds);
    const model = path.basename(config.whisper.modelPath);
    const vad = config.whisper.vadModelPath !== null;

    observeQuality(event, { model, rtf, wordsPerSecond, wordCount, durationSeconds });

    let transcriptFileId: string | null = null;
    let transcriptJsonFileId: string | null = null;
    if (event.output === 'file') {
      const txt = formatTranscriptTxt(segments);
      const json = JSON.stringify(buildTranscriptJsonV2({ segments, language: detectedLanguage, model, vad, durationSeconds }));
      logger.info({ eventId: event.eventId, segments: segments.length }, 'uploading transcript (txt + json)');
      transcriptFileId = await stage('UploadFailed', () => uploadTranscriptFile(ctx, txt, 'txt', 'text/plain'));
      transcriptJsonFileId = await stage('UploadFailed', () =>
        uploadTranscriptFile(ctx, json, 'json', 'application/json'),
      );
    }

    // Sin reason dedicado para "fallo al publicar": se reusa 'Timeout' (el
    // trabajo ya esta subido, lo que se pierde es la notificacion).
    await stage('Timeout', async () =>
      publishTranscriptReady({
        kind: event.kind,
        tenantId: event.tenantId,
        correlationId: event.correlationId,
        targetId: event.targetId,
        recordingFileId: event.recordingFileId,
        passthrough: event.passthrough,
        detectedLanguage,
        durationSeconds,
        wordCount,
        model,
        vad,
        realtimeFactor: rtf,
        wordsPerSecond,
        output:
          event.output === 'file' && transcriptFileId
            ? {
                type: 'file',
                transcriptFileId,
                transcriptJsonFileId,
                segmentCount: segments.length,
                speakerCount: collectSpeakers(segments).length,
              }
            : { type: 'inline', transcriptText: truncateInlineText(text, config.inline.maxChars) },
      }),
    );

    logger.info(
      { eventId: event.eventId, targetId: event.targetId, transcriptFileId, transcriptJsonFileId, output: event.output },
      'transcript ready',
    );
  } catch (err) {
    status = 'failure';
    if (err instanceof StageFailure) {
      reportTranscriptFailed(event, err.reason, err.original);
      throw err.original;
    }
    throw err;
  } finally {
    stopTimer({ status });
    await rm(workDir, { recursive: true, force: true }).catch((err: unknown) =>
      logger.warn({ err: (err as Error).message, workDir }, 'temp dir cleanup failed'),
    );
  }
}

function observeQuality(
  event: RecordingReadyEvent,
  input: { model: string; rtf: number | null; wordsPerSecond: number | null; wordCount: number; durationSeconds: number },
): void {
  if (input.rtf !== null) realtimeFactor.observe({ kind: event.kind, model: input.model }, input.rtf);
  const anomaly = classifyWordsPerSecond(input.wordsPerSecond, input.durationSeconds, {
    min: config.quality.wpsMin,
    max: config.quality.wpsMax,
    minDurationSeconds: config.quality.wpsMinDurationSeconds,
  });
  if (anomaly) {
    wordsPerSecondAnomaliesTotal.inc({ kind: event.kind, direction: anomaly });
    logger.warn(
      {
        eventId: event.eventId,
        targetId: event.targetId,
        wordsPerSecond: input.wordsPerSecond,
        wordCount: input.wordCount,
        durationSeconds: input.durationSeconds,
        direction: anomaly,
      },
      'transcript words/second out of range (empty recording or hallucination?)',
    );
  }
}

/**
 * Publica TranscriptFailed y loguea. Nunca lanza: si el publish falla, el
 * error ORIGINAL del stage sigue siendo el que propaga.
 */
function reportTranscriptFailed(event: RecordingReadyEvent, reason: TranscriptFailureReason, err: Error): void {
  logger.error(
    { err: err.message, eventId: event.eventId, targetId: event.targetId, reason },
    'transcript pipeline stage failed',
  );
  pipelineFailuresTotal.inc({ reason, kind: event.kind });
  if (reason === 'TooLong') tooLongTotal.inc({ kind: event.kind });
  try {
    publishTranscriptFailed({
      kind: event.kind,
      tenantId: event.tenantId,
      correlationId: event.correlationId,
      targetId: event.targetId,
      recordingFileId: event.recordingFileId,
      passthrough: event.passthrough,
      failureReason: reason,
      errorMessage: err.message,
    });
  } catch (publishErr) {
    logger.warn(
      { err: (publishErr as Error).message, eventId: event.eventId },
      'publishTranscriptFailed itself failed — original stage error still propagates',
    );
  }
}
