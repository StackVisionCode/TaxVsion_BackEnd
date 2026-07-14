import { randomUUID } from 'node:crypto';
import { mkdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { CloudStorageClient } from './cloudstorage/cloudstorage-client.js';
import type { RecordingReadyEvent } from './rabbit/consumer.js';
import { publishTranscriptReady } from './rabbit/publisher.js';
import { transcodeToWav16kMono } from './media/audio-transcoder.js';
import { transcribeWav } from './whisper/whisper-transcriber.js';
import { config } from './config.js';
import { logger } from './logger.js';

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
 */
export async function processRecordingReady(
  event: RecordingReadyEvent,
  deps: { cloudStorage: CloudStorageClient },
): Promise<void> {
  const workDir = path.join(config.whisper.tempDir, event.eventId);
  await mkdir(workDir, { recursive: true });

  try {
    const originalPath = path.join(workDir, 'recording.bin');
    const wavPath = path.join(workDir, 'audio.wav');
    const txtOutPrefix = path.join(workDir, 'transcript');

    logger.info({ eventId: event.eventId, kind: event.kind, targetId: event.targetId }, 'downloading recording');
    await deps.cloudStorage.downloadFile(event.tenantId, event.recordingFileId, originalPath);

    logger.info({ eventId: event.eventId }, 'transcoding to wav');
    await transcodeToWav16kMono(originalPath, wavPath);

    logger.info({ eventId: event.eventId }, 'running whisper.cpp');
    const { text, language } = await transcribeWav(wavPath, txtOutPrefix);

    const transcriptFileName = `${event.kind}-${event.targetId}-transcript-${randomUUID()}.txt`;
    const transcriptPath = path.join(workDir, transcriptFileName);
    await writeFile(transcriptPath, text, 'utf-8');

    logger.info({ eventId: event.eventId }, 'uploading transcript');
    const uploaded = await deps.cloudStorage.uploadFile({
      tenantId: event.tenantId,
      filePath: transcriptPath,
      originalName: transcriptFileName,
      contentType: 'text/plain',
      sizeBytes: Buffer.byteLength(text, 'utf-8'),
      ownerId: event.targetId,
      correlationId: event.correlationId,
    });

    publishTranscriptReady({
      kind: event.kind,
      tenantId: event.tenantId,
      correlationId: event.correlationId,
      targetId: event.targetId,
      recordingFileId: event.recordingFileId,
      transcriptFileId: uploaded.fileId,
      language,
    });

    logger.info(
      { eventId: event.eventId, targetId: event.targetId, transcriptFileId: uploaded.fileId },
      'transcript ready',
    );
  } finally {
    await rm(workDir, { recursive: true, force: true }).catch((err: unknown) =>
      logger.warn({ err: (err as Error).message, workDir }, 'temp dir cleanup failed'),
    );
  }
}
