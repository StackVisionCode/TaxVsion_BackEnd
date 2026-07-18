import { randomUUID } from 'node:crypto';
import { mkdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { CloudStorageClient } from './cloudstorage/cloudstorage-client.js';
import { DownloadStatusError } from './cloudstorage/cloudstorage-client.js';
import type { RecordingReadyEvent } from './rabbit/consumer.js';
import { publishTranscriptReady, publishTranscriptFailed } from './rabbit/publisher.js';
import { transcodeToWav16kMono, probeAudioStreams } from './media/audio-transcoder.js';
import { transcribeWav } from './whisper/whisper-transcriber.js';
import { config } from './config.js';
import { logger } from './logger.js';
import type { TranscriptFailureReason } from './contracts/events.js';
import { withRetry } from './retry.js';
import { pipelineDurationSeconds, pipelineFailuresTotal } from './telemetry/metrics.js';

// Fase Transcript 4 — solo download y upload reintentan (fallas transientes
// reales); ffmpeg/whisper NUNCA (fallas deterministicas, ver docblock mas
// abajo). "hasta 3 intentos" = 3 REINTENTOS ademas del intento original = 4
// intentos totales (config.retry.maxAttempts, default 4), con el ultimo ya
// sin mas backoff antes de reportar+nack. Configurable por env (ver
// config.ts) para poder acortar el backoff en tests sin tocar codigo.
const RETRY_MAX_ATTEMPTS = config.retry.maxAttempts;
const RETRY_BACKOFF_MS = config.retry.backoffMs;

// 'File.NotAvailable' = el archivo todavia esta en ClamAV scan (CloudStorage
// rechaza download-url con 403 hasta que Status pase a Available) — carrera
// transitoria contra `recording_processing_started.v1` (se publica apenas
// termina el upload, sin esperar el scan async), se resuelve sola en 1-3s.
// Otros 403 (ej. 'File.Forbidden', mismatch real de scope) NO son retriables
// a proposito, igual que el resto de errores de permiso/config — ver docblock
// de RETRIABLE_UPLOAD_CODES mas abajo.
function isRetriableDownloadError(err: unknown): boolean {
  if (!(err instanceof DownloadStatusError)) return false;
  return err.status >= 500 || err.errorCode === 'File.NotAvailable';
}

// Codigos tipicos de blip transiente hacia MinIO: errores de red de Node
// (ECONNRESET/ETIMEDOUT/ECONNREFUSED/EPIPE/EAI_AGAIN) o codigos S3-style que
// el SDK de MinIO expone en `err.code` para 5xx/backpressure del servidor.
// Errores de credenciales/permiso (AccessDenied, InvalidAccessKeyId, etc.) o
// bucket/policy mal configurados NO estan en esta lista a proposito — un
// retry no los arregla, solo demora el nack 36s de mas.
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

/**
 * Orquesta un `RecordingReadyEvent`:
 *   1. Descarga la grabacion original desde CloudStorage.
 *   2. Transcodifica a WAV 16kHz mono con ffmpeg (whisper.cpp no lee webm/opus).
 *   3. Transcribe con whisper.cpp.
 *   4. Sube el .txt resultante a CloudStorage (OwnerType Communication /
 *      FolderType Recordings) — el propio call/meeting es el "owner" logico
 *      del transcript, no hay otro OwnerId natural disponible aca.
 *   5. Publica `TranscriptReady` para que Communication lo adjunte.
 *
 * Los archivos temporales viven bajo `config.whisper.tempDir/{eventId}` y se
 * borran siempre, exito o error (evita llenar disco en un pod de larga vida).
 *
 * Fase Transcript 2 — state awareness: todos los stages (incluido el chequeo
 * de audio, ver Fase Transcript 3 mas abajo) estan envueltos en su propio
 * try/catch. Cada catch identifica el `TranscriptFailureReason`
 * correspondiente al stage, publica `TranscriptFailed` (para que
 * Communication/Analytics se enteren de CUAL grabacion fallo y POR QUE, algo
 * que antes no pasaba — el error solo terminaba en un nack silencioso a la
 * DLQ) y vuelve a lanzar el error original SIN modificarlo, para que
 * `consumer.ts` siga haciendo exactamente lo que ya hacia (log +
 * inbox.unmark + nack a DLQ). No hay reintentos in-worker aca — la DLQ sigue
 * siendo la unica superficie de retry.
 *
 * Fase Transcript 3 — el chequeo de audio (`probeAudioStreams`, antes de
 * transcodificar) reemplaza al de Fase Backend 8: ya no hace ack-and-forget
 * cuando no hay audio, sigue el mismo camino report+rethrow que el resto.
 *
 * Fase Transcript 4 — download y upload (los 2 stages con fallas realmente
 * transientes: HTTP 5xx, blips de red a MinIO) reintentan hasta 3 veces con
 * backoff (1s/5s/30s) ANTES de reportar+relanzar — ver retry.ts. ffprobe/
 * ffmpeg/whisper siguen sin ningun retry (fallas deterministicas: un exit
 * code de ffmpeg no cambia si se reintenta).
 *
 * Fase Transcript 8 — observabilidad: `pipelineDurationSeconds` mide el
 * metodo completo (labels kind/status, un timer por invocacion, incluye
 * reintentos), `pipelineFailuresTotal` se incrementa en `reportTranscriptFailed`
 * (labels reason/kind) — ver telemetry/metrics.ts.
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
    const originalPath = path.join(workDir, 'recording.bin');
    const wavPath = path.join(workDir, 'audio.wav');
    const txtOutPrefix = path.join(workDir, 'transcript');

    logger.info({ eventId: event.eventId, kind: event.kind, targetId: event.targetId }, 'downloading recording');
    try {
      await withRetry(() => deps.cloudStorage.downloadFile(event.tenantId, event.recordingFileId, originalPath), {
        maxAttempts: RETRY_MAX_ATTEMPTS,
        backoffMs: RETRY_BACKOFF_MS,
        isRetriable: isRetriableDownloadError,
        onRetry: (attempt, err, delayMs) =>
          logger.warn(
            { eventId: event.eventId, attempt, err: (err as Error).message, delayMs },
            'download failed (retriable); retrying',
          ),
      });
    } catch (err) {
      reportTranscriptFailed(event, 'DownloadFailed', err as Error);
      throw err;
    }

    // Fase Transcript 3 (bug #245) — validar audio antes de gastar CPU/tiempo
    // en el transcode. Reemplaza el chequeo equivalente de Fase Backend 8
    // (hasAudioStream + RecordingValidationFailed, ver docblock de
    // probeAudioStreams): a diferencia de esa version, esta SI relanza el
    // error (nack -> DLQ) en vez de ack-and-forget — una grabacion sin audio
    // ahora queda disponible para inspeccion/reproceso manual igual que
    // cualquier otro fallo del pipeline, no se descarta silenciosamente.
    try {
      await probeAudioStreams(originalPath);
    } catch (err) {
      reportTranscriptFailed(event, 'NoAudioStream', err as Error);
      throw err;
    }

    logger.info({ eventId: event.eventId }, 'transcoding to wav');
    try {
      await transcodeToWav16kMono(originalPath, wavPath);
    } catch (err) {
      reportTranscriptFailed(event, 'FfmpegError', err as Error);
      throw err;
    }

    logger.info({ eventId: event.eventId }, 'running whisper.cpp');
    let text: string;
    let detectedLanguage: string | null;
    let durationSeconds: number;
    let wordCount: number;
    try {
      ({ text, detectedLanguage, durationSeconds, wordCount } = await transcribeWav(wavPath, txtOutPrefix));
    } catch (err) {
      reportTranscriptFailed(event, 'WhisperError', err as Error);
      throw err;
    }

    const transcriptFileName = `${event.kind}-${event.targetId}-transcript-${randomUUID()}.txt`;
    const transcriptPath = path.join(workDir, transcriptFileName);
    await writeFile(transcriptPath, text, 'utf-8');

    logger.info({ eventId: event.eventId }, 'uploading transcript');
    let uploaded: { fileId: string };
    try {
      uploaded = await withRetry(
        () =>
          deps.cloudStorage.uploadFile({
            tenantId: event.tenantId,
            filePath: transcriptPath,
            originalName: transcriptFileName,
            contentType: 'text/plain',
            sizeBytes: Buffer.byteLength(text, 'utf-8'),
            ownerId: event.targetId,
            correlationId: event.correlationId,
          }),
        {
          maxAttempts: RETRY_MAX_ATTEMPTS,
          backoffMs: RETRY_BACKOFF_MS,
          isRetriable: isRetriableUploadError,
          onRetry: (attempt, err, delayMs) =>
            logger.warn(
              { eventId: event.eventId, attempt, err: (err as Error).message, delayMs },
              'upload failed (retriable); retrying',
            ),
        },
      );
    } catch (err) {
      reportTranscriptFailed(event, 'UploadFailed', err as Error);
      throw err;
    }

    try {
      publishTranscriptReady({
        kind: event.kind,
        tenantId: event.tenantId,
        correlationId: event.correlationId,
        targetId: event.targetId,
        recordingFileId: event.recordingFileId,
        transcriptFileId: uploaded.fileId,
        detectedLanguage,
        durationSeconds,
        wordCount,
      });
    } catch (err) {
      // No hay un reason dedicado para "fallo al publicar" en el contrato de
      // Fase Transcript 1 (solo NoAudioStream/FfmpegError/WhisperError/
      // DownloadFailed/UploadFailed/Timeout) — 'Timeout' es el unico valor
      // que sobra de los 6 definidos, se reusa aca como bucket generico. El
      // transcript YA esta subido a CloudStorage en este punto; lo que se
      // pierde es solo la notificacion, no el trabajo — igual se reporta.
      reportTranscriptFailed(event, 'Timeout', err as Error);
      throw err;
    }

    logger.info(
      { eventId: event.eventId, targetId: event.targetId, transcriptFileId: uploaded.fileId },
      'transcript ready',
    );
  } catch (err) {
    status = 'failure';
    throw err;
  } finally {
    stopTimer({ status });
    await rm(workDir, { recursive: true, force: true }).catch((err: unknown) =>
      logger.warn({ err: (err as Error).message, workDir }, 'temp dir cleanup failed'),
    );
  }
}

/**
 * Publica `TranscriptFailed` con el reason identificado por el stage que
 * llama, y loguea. Nunca lanza: si el publish mismo falla (ej. canal de
 * Rabbit caido), se loguea aparte y se deja que el error ORIGINAL del stage
 * (no este) sea el que propague — perder la notificacion es aceptable,
 * perder la causa raiz del error no.
 */
function reportTranscriptFailed(event: RecordingReadyEvent, reason: TranscriptFailureReason, err: Error): void {
  logger.error(
    { err: err.message, eventId: event.eventId, targetId: event.targetId, reason },
    'transcript pipeline stage failed',
  );
  pipelineFailuresTotal.inc({ reason, kind: event.kind });
  try {
    publishTranscriptFailed({
      kind: event.kind,
      tenantId: event.tenantId,
      correlationId: event.correlationId,
      targetId: event.targetId,
      recordingFileId: event.recordingFileId,
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
