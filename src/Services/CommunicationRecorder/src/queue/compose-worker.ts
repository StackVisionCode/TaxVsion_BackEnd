import { randomUUID } from 'node:crypto';
import { rm, stat } from 'node:fs/promises';
import path from 'node:path';
import type { Logger } from 'pino';
import { composeRecording, fallbackTimeline, resolveInput, type UsableTrack } from '../compose/composer.js';
import type { AvatarSource } from '../compose/avatars.js';
import type { KeyframeSizeProber } from '../compose/normalize.js';
import type { FfmpegRunner, Prober } from '../compose/ffmpeg-runner.js';
import {
  buildComposedEvent,
  buildCompositionFailedEvent,
  buildSaveFileRequested,
  composedFileName,
  trackFileName,
  type ComposedAudioTrack,
  type FolderType,
} from '../contracts/events.js';
import type { EventBus } from '../rabbit/event-bus.js';
import { retainUntil } from '../spool/janitor.js';
import type { CompositionResult, FailureReason, SessionManifest } from '../spool/manifest.js';
import type { KeyedMutex, SpoolStore } from '../spool/spool-store.js';
import type { RecorderMetrics } from '../telemetry/metrics.js';
import type { ObjectUploader } from '../upload/object-uploader.js';
import { isTransientError, withRetry } from '../upload/retry.js';
import type { ComposeJob, ComposeQueue } from './compose-queue.js';

export interface ComposeWorkerDeps {
  readonly store: SpoolStore;
  readonly locks: KeyedMutex;
  readonly queue: Pick<ComposeQueue, 'take' | 'ack' | 'requeue'>;
  readonly bus: EventBus;
  readonly uploader: ObjectUploader;
  readonly runner: FfmpegRunner;
  readonly probe: Prober;
  readonly metrics: RecorderMetrics;
  readonly logger: Logger;
  /** Fotos de perfil para el slate; null = iniciales. */
  readonly avatars?: AvatarSource | null;
  /** Tamaños de keyframes (normalización de pistas que cambian de resolución). */
  readonly sizes?: KeyframeSizeProber;
  readonly settings: {
    readonly fontFile: string;
    readonly threads: number;
    readonly timeoutFactor: number;
    readonly timeoutMinMinutes: number;
    readonly sourcePrefix: string;
    readonly externalUploadsQueue: string;
    readonly retry: { readonly maxAttempts: number; readonly backoffMs: readonly number[] };
    /** Espera antes de reencolar un trabajo cuyo evento final no se pudo publicar. */
    readonly requeueDelayMs?: number;
  };
  readonly now?: () => Date;
  readonly newId?: () => string;
  readonly sleep?: (ms: number) => Promise<void>;
}

export type JobOutcome = 'done' | 'retry';

class StageError extends Error {
  constructor(
    readonly reason: FailureReason,
    message: string,
  ) {
    super(message);
  }
}

/**
 * Consumidor de la cola con concurrencia 1: compone, sube (pistas de audio primero, luego el
 * compuesto), publica `composed.v1` y solo entonces hace ack. Cualquier fallo → `composition_failed`.
 */
export class ComposeWorker {
  private stopping = false;
  private loop: Promise<void> | undefined;
  private readonly now: () => Date;
  private readonly newId: () => string;
  private readonly sleep: (ms: number) => Promise<void>;

  constructor(private readonly deps: ComposeWorkerDeps) {
    this.now = deps.now ?? (() => new Date());
    this.newId = deps.newId ?? randomUUID;
    this.sleep = deps.sleep ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms)));
  }

  start(): void {
    this.loop = this.run();
  }

  async stop(drainMs: number): Promise<void> {
    this.stopping = true;
    if (!this.loop) return;
    await Promise.race([this.loop, this.sleep(drainMs)]);
  }

  private async run(): Promise<void> {
    while (!this.stopping) {
      let item: Awaited<ReturnType<ComposeWorkerDeps['queue']['take']>>;
      try {
        item = await this.deps.queue.take(5);
      } catch (err) {
        this.deps.logger.error({ err: (err as Error).message }, 'compose queue unavailable');
        await this.sleep(2000);
        continue;
      }
      if (!item) continue;
      try {
        const outcome = await this.processJob(item.job);
        if (outcome === 'done') await this.deps.queue.ack(item.raw);
        else {
          await this.sleep(this.deps.settings.requeueDelayMs ?? 30_000);
          await this.deps.queue.requeue(item.raw);
        }
      } catch (err) {
        // Queda en "en curso" y se recupera al reiniciar.
        this.deps.logger.error({ jobId: item.job.jobId, err: (err as Error).message }, 'compose job crashed');
      }
    }
  }

  async processJob(job: ComposeJob): Promise<JobOutcome> {
    const log = this.deps.logger.child({ sessionId: job.sessionId, jobId: job.jobId });
    const manifest = await this.deps.store.readManifest(job.sessionId);
    if (!manifest || manifest.state === 'aborted' || manifest.jobId !== job.jobId) {
      log.info('job skipped (session gone, aborted or superseded)');
      return 'done';
    }
    if (manifest.state === 'composed' && manifest.result?.jobId === job.jobId) return 'done';
    if (manifest.state === 'failed') return 'done';

    await this.update(job.sessionId, (m) => {
      m.state = 'composing';
    });
    const sessionDir = this.deps.store.sessionDir(job.sessionId);
    const ext = manifest.composition.format;
    const outputPath = path.join(sessionDir, `composed-${job.jobId}.${ext}`);
    const workDir = path.join(sessionDir, `compose-${job.jobId}`);
    const started = Date.now();

    let result = manifest.result?.jobId === job.jobId ? manifest.result : undefined;
    let audioTracks: ComposedAudioTrack[];
    try {
      const resolved = await resolveInput(
        manifest,
        (file) => this.deps.store.trackPath(job.sessionId, file),
        (file) => this.deps.store.fileSize(file),
        this.deps.probe,
      );
      if (resolved.tracks.length === 0 || resolved.windowMs <= 0) {
        throw new StageError('NoTracks', 'No usable tracks were captured.');
      }
      if (!result) {
        const timeline = (await this.deps.store.readTimeline(job.sessionId)) ?? fallbackTimeline(manifest);
        const timeoutMs = Math.max(
          this.deps.settings.timeoutMinMinutes * 60_000,
          resolved.windowMs * this.deps.settings.timeoutFactor,
        );
        let outcome;
        try {
          outcome = await composeRecording(
            {
              startedAtUtc: manifest.startedAtUtc,
              captureVideo: manifest.captureVideo,
              composition: manifest.composition,
              timeline,
              windowMs: resolved.windowMs,
              tracks: resolved.tracks,
              workDir,
              outputPath,
            },
            {
              runner: this.deps.runner,
              fontFile: this.deps.settings.fontFile,
              threads: this.deps.settings.threads,
              timeoutMs,
              avatars: this.deps.avatars ?? null,
              ...(this.deps.sizes ? { sizes: this.deps.sizes } : {}),
              onAvatarError: (fileId, err) =>
                log.warn({ fileId, err: (err as Error).message }, 'avatar unavailable; using initials'),
            },
          );
        } catch (err) {
          throw new StageError('ComposeError', (err as Error).message);
        }
        const probed = await this.deps.probe(outputPath).catch(() => null);
        const durationMs = probed?.durationMs ?? resolved.windowMs;
        const wallSeconds = (Date.now() - started) / 1000;
        this.deps.metrics.composeDurationSeconds.observe({ format: ext }, wallSeconds);
        if (wallSeconds > 0) this.deps.metrics.composeRealtimeFactor.observe(durationMs / 1000 / wallSeconds);
        log.info({ segments: outcome.segments, durationMs, wallSeconds }, 'composition rendered');

        audioTracks = await this.uploadAudioTracks(manifest, resolved.tracks);
        const composedFileId = this.newId();
        const sizeBytes = (await stat(outputPath)).size;
        await this.uploadFile(manifest, {
          fileId: composedFileId,
          filePath: outputPath,
          folderType: 'Recordings',
          contentType: ext === 'mp4' ? 'video/mp4' : 'video/webm',
          originalName: composedFileName(manifest.scope, manifest.startedAtUtc, ext),
          sizeBytes,
        });
        const composedResult: CompositionResult = {
          jobId: job.jobId,
          composedFileId,
          composedSizeBytes: sizeBytes,
          format: ext,
          durationSeconds: Math.round(durationMs) / 1000,
          composedAtUtc: this.now().toISOString(),
          videoTrackCount: outcome.videoTrackCount,
        };
        result = composedResult;
        await this.update(job.sessionId, (m) => {
          m.result = composedResult;
        });
        await rm(outputPath, { force: true });
        await rm(workDir, { recursive: true, force: true });
      } else {
        audioTracks = await this.uploadAudioTracks(manifest, resolved.tracks);
      }
    } catch (err) {
      await rm(workDir, { recursive: true, force: true }).catch(() => undefined);
      await rm(outputPath, { force: true }).catch(() => undefined);
      const current = await this.deps.store.readManifest(job.sessionId);
      if (!current || current.state === 'aborted') return 'done';
      const reason = err instanceof StageError ? err.reason : 'ComposeError';
      log.error({ reason, err: (err as Error).message }, 'composition failed');
      return this.fail(current, reason, (err as Error).message);
    }

    const finalResult = result;
    const event = buildComposedEvent(manifest, {
      eventId: this.newId(),
      occurredOnUtc: this.now().toISOString(),
      composedFileId: finalResult.composedFileId,
      composedSizeBytes: finalResult.composedSizeBytes,
      format: finalResult.format,
      durationSeconds: finalResult.durationSeconds,
      audioTracks,
      videoTrackCount: finalResult.videoTrackCount,
      composedAtUtc: finalResult.composedAtUtc,
    });
    try {
      await this.publish(event.eventType, event.eventId, event);
    } catch (err) {
      log.error({ err: (err as Error).message }, 'composed event not published; job will be retried');
      return 'retry';
    }
    await this.update(job.sessionId, (m) => {
      m.state = 'composed';
      m.retainUntilUtc = retainUntil(this.now(), m.tracksRetentionDays);
    });
    this.deps.metrics.compositionsTotal.inc({ format: finalResult.format });
    log.info({ composedFileId: finalResult.composedFileId }, 'recording composed and published');
    return 'done';
  }

  private async fail(manifest: SessionManifest, reason: FailureReason, message: string): Promise<JobOutcome> {
    const event = buildCompositionFailedEvent(manifest, {
      eventId: this.newId(),
      occurredOnUtc: this.now().toISOString(),
      reason,
      errorMessage: message,
    });
    try {
      await this.publish(event.eventType, event.eventId, event);
    } catch {
      return 'retry';
    }
    await this.update(manifest.sessionId, (m) => {
      m.state = 'failed';
      m.failure = { reason, message: message.slice(0, 2000), atUtc: this.now().toISOString() };
      m.retainUntilUtc = retainUntil(this.now(), m.tracksRetentionDays);
    });
    this.deps.metrics.failuresTotal.inc({ reason });
    return 'done';
  }

  /** Solo audio va a CloudStorage (el transcript lo necesita); al recomponer se reutilizan los fileIds. */
  private async uploadAudioTracks(manifest: SessionManifest, tracks: readonly UsableTrack[]): Promise<ComposedAudioTrack[]> {
    const out: ComposedAudioTrack[] = [];
    for (const t of tracks.filter((x) => x.kind === 'audio')) {
      let fileId = t.manifest.fileId;
      const durationMs = t.manifest.durationMs ?? t.probedDurationMs ?? t.endMs - t.startMs;
      if (!fileId) {
        const newFileId = this.newId();
        await this.uploadFile(manifest, {
          fileId: newFileId,
          filePath: t.file,
          folderType: 'RecordingTracks',
          contentType: 'audio/webm',
          originalName: trackFileName(t.userId, t.trackId),
          sizeBytes: (await stat(t.file)).size,
        });
        await this.update(manifest.sessionId, (m) => {
          const mt = m.tracks.find((x) => x.trackId === t.trackId);
          if (mt) {
            mt.fileId = newFileId;
            mt.durationMs = durationMs;
          }
        });
        fileId = newFileId;
      }
      out.push({ trackId: t.trackId, userId: t.userId, fileId, source: t.source, startOffsetMs: t.startMs, durationMs });
    }
    return out;
  }

  private async uploadFile(
    manifest: SessionManifest,
    f: { fileId: string; filePath: string; folderType: FolderType; contentType: string; originalName: string; sizeBytes: number },
  ): Promise<void> {
    const extension = path.extname(f.filePath) || '.bin';
    const objectKey = [this.deps.settings.sourcePrefix, manifest.tenantId, manifest.sessionId, `${f.fileId}${extension}`]
      .filter((s) => s.length > 0)
      .join('/');
    const retry = {
      maxAttempts: this.deps.settings.retry.maxAttempts,
      backoffMs: this.deps.settings.retry.backoffMs,
      sleep: this.sleep,
    };
    try {
      await withRetry(() => this.deps.uploader.putFile(objectKey, f.filePath, f.contentType), {
        ...retry,
        isRetriable: isTransientError,
        onRetry: (attempt, err, delayMs) =>
          this.deps.logger.warn({ attempt, delayMs, err: (err as Error).message }, 'upload failed (retriable); retrying'),
      });
      const eventId = this.newId();
      const body = buildSaveFileRequested({
        eventId,
        occurredOn: this.now().toISOString(),
        tenantId: manifest.tenantId,
        correlationId: manifest.correlationId,
        fileId: f.fileId,
        sourceBucket: this.deps.uploader.bucket,
        sourceObjectKey: objectKey,
        ownerId: manifest.scopeId,
        folderType: f.folderType,
        originalName: f.originalName,
        contentType: f.contentType,
        sizeBytes: f.sizeBytes,
      });
      await withRetry(() => this.deps.bus.sendToQueue(this.deps.settings.externalUploadsQueue, eventId, body), {
        ...retry,
        isRetriable: () => true,
      });
    } catch (err) {
      throw new StageError('UploadFailed', (err as Error).message);
    }
  }

  private async publish(eventType: string, eventId: string, body: object): Promise<void> {
    await withRetry(() => this.deps.bus.publishEvent(eventType, eventId, body), {
      maxAttempts: this.deps.settings.retry.maxAttempts,
      backoffMs: this.deps.settings.retry.backoffMs,
      isRetriable: () => true,
      sleep: this.sleep,
    });
  }

  private async update(sessionId: string, mutate: (m: SessionManifest) => void): Promise<void> {
    await this.deps.locks.run(sessionId, async () => {
      const m = await this.deps.store.readManifest(sessionId);
      // Un abort concurrente gana: no se resucita la sesión.
      if (!m || m.state === 'aborted') return;
      mutate(m);
      m.updatedAtUtc = this.now().toISOString();
      await this.deps.store.writeManifest(m);
    });
  }
}
