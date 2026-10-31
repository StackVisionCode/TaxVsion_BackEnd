import { randomUUID } from 'node:crypto';
import { writeFile } from 'node:fs/promises';
import type { Logger } from 'pino';
import type { CreateSessionRequest, OpenTrackRequest, RecomposeRequest, StopSessionRequest } from '../contracts/api.js';
import { Errors } from '../contracts/errors.js';
import { buildCompositionFailedEvent } from '../contracts/events.js';
import type { ComposeJob } from '../queue/compose-queue.js';
import type { EventBus } from '../rabbit/event-bus.js';
import { retainUntil } from '../spool/janitor.js';
import type { FailureReason, SessionManifest } from '../spool/manifest.js';
import type { KeyedMutex, SpoolStore } from '../spool/spool-store.js';
import type { RecorderMetrics } from '../telemetry/metrics.js';
import { withRetry } from '../upload/retry.js';
import { CaptureStartError, byeTarget, type CaptureHandle, type CaptureLauncher } from './ffmpeg-capture.js';
import { PortsExhaustedError, type PortAllocator } from './port-allocator.js';
import { buildSdp } from './sdp.js';

export interface JobSink {
  enqueue(job: ComposeJob): Promise<void>;
  contains(jobId: string): Promise<boolean>;
  recover(): Promise<number>;
}

export interface SessionManagerDeps {
  readonly store: SpoolStore;
  readonly locks: KeyedMutex;
  readonly jobs: JobSink;
  readonly bus: EventBus;
  readonly ports: PortAllocator;
  readonly launcher: CaptureLauncher;
  readonly metrics: RecorderMetrics;
  readonly logger: Logger;
  readonly settings: {
    readonly announcedIp: string | null;
    readonly listenIp: string;
    readonly maxActiveSessions: number;
    readonly stopTimeoutMs: number;
    readonly orphanTimeoutMinutes: number;
    readonly retry: { readonly maxAttempts: number; readonly backoffMs: readonly number[] };
  };
  readonly now?: () => Date;
  readonly newId?: () => string;
}

interface LiveTrack {
  readonly handle: CaptureHandle;
  readonly rtpPort: number;
}

interface LiveSession {
  readonly sessionId: string;
  readonly tracks: Map<string, LiveTrack>;
  /** trackIds con apertura en curso (fuera del lock mientras arranca ffmpeg). */
  readonly opening: Set<string>;
  hardStopTimer?: NodeJS.Timeout;
  orphanTimer?: NodeJS.Timeout;
}

const MAX_TIMER_MS = 2_147_000_000;

/**
 * Ciclo de vida de las sesiones de captura. No conoce reglas de negocio: Communication decide qué se
 * graba y cuándo para; aquí solo se abren/cierran ffmpeg por pista, se lleva el manifest y se encola
 * la composición.
 */
export class SessionManager {
  private readonly live = new Map<string, LiveSession>();
  private readonly now: () => Date;
  private readonly newId: () => string;

  constructor(private readonly deps: SessionManagerDeps) {
    this.now = deps.now ?? (() => new Date());
    this.newId = deps.newId ?? randomUUID;
  }

  get activeSessions(): number {
    return this.live.size;
  }

  get activeTracks(): number {
    let n = 0;
    for (const s of this.live.values()) n += s.tracks.size;
    return n;
  }

  async createSession(req: CreateSessionRequest, correlationId: string | undefined): Promise<{ created: boolean }> {
    return this.deps.locks.run(req.sessionId, async () => {
      if (this.live.has(req.sessionId) || (await this.deps.store.readManifest(req.sessionId))) return { created: false };
      if (this.live.size >= this.deps.settings.maxActiveSessions) throw Errors.capacityExceeded();
      // Reserva síncrona tras la comprobación: dos altas concurrentes no superan el máximo.
      const session: LiveSession = { sessionId: req.sessionId, tracks: new Map(), opening: new Set() };
      this.live.set(req.sessionId, session);
      try {
        const nowIso = this.now().toISOString();
        const manifest: SessionManifest = {
          version: 1,
          sessionId: req.sessionId,
          tenantId: req.tenantId,
          scope: req.scope,
          scopeId: req.scopeId,
          startedAtUtc: new Date(req.startedAtUtc).toISOString(),
          maxDurationSeconds: req.maxDurationSeconds,
          toleranceSeconds: req.toleranceSeconds,
          tracksRetentionDays: req.tracksRetentionDays,
          captureVideo: req.captureVideo,
          composition: req.composition,
          correlationId: correlationId ?? req.sessionId,
          createdAtUtc: nowIso,
          state: 'capturing',
          tracks: [],
          updatedAtUtc: nowIso,
        };
        await this.deps.store.createSessionDir(req.sessionId);
        await this.deps.store.writeManifest(manifest);
        this.scheduleHardStop(session, manifest);
      } catch (err) {
        this.live.delete(req.sessionId);
        throw err;
      }
      this.updateGauges();
      this.deps.logger.info({ sessionId: req.sessionId, scope: req.scope }, 'recording session started');
      return { created: true };
    });
  }

  async openTrack(
    sessionId: string,
    req: OpenTrackRequest,
  ): Promise<{ created: boolean; ip: string | null; port: number; rtcpPort: number }> {
    const reserved = await this.deps.locks.run(sessionId, async () => {
      const manifest = await this.deps.store.readManifest(sessionId);
      if (!manifest) throw Errors.sessionNotFound();
      const session = this.live.get(sessionId);
      if (manifest.state !== 'capturing' || !session) throw Errors.sessionClosed();
      if (req.kind === 'video' && !manifest.captureVideo) throw Errors.videoNotCaptured();
      const existing = manifest.tracks.find((t) => t.trackId === req.trackId);
      if (existing) {
        if (session.tracks.has(req.trackId)) {
          return { reused: true as const, port: existing.rtpPort, rtcpPort: existing.rtcpPort };
        }
        throw Errors.trackClosed();
      }
      if (session.opening.has(req.trackId)) throw Errors.trackOpening();
      let pair;
      try {
        pair = await this.deps.ports.allocate();
      } catch (err) {
        if (err instanceof PortsExhaustedError) throw Errors.portsExhausted();
        throw err;
      }
      session.opening.add(req.trackId);
      return { reused: false as const, port: pair.rtpPort, rtcpPort: pair.rtcpPort, manifest, session };
    });
    if (reserved.reused) {
      return { created: false, ip: this.deps.settings.announcedIp, port: reserved.port, rtcpPort: reserved.rtcpPort };
    }

    const { manifest, session } = reserved;
    const file = `${req.trackId}.webm`;
    let handle: CaptureHandle;
    try {
      const sdpPath = this.deps.store.trackPath(sessionId, `${req.trackId}.sdp`);
      await writeFile(
        sdpPath,
        buildSdp({
          listenIp: this.deps.settings.listenIp,
          kind: req.kind,
          rtpPort: reserved.port,
          rtcpPort: reserved.rtcpPort,
          codec: req.codec,
          ssrc: req.ssrc,
        }),
        'utf-8',
      );
      handle = await this.deps.launcher.launch({
        sdpPath,
        outputPath: this.deps.store.trackPath(sessionId, file),
        logPath: this.deps.store.trackPath(sessionId, `${req.trackId}.log`),
        rtcp: { ip: byeTarget(this.deps.settings.listenIp), port: reserved.rtcpPort, ssrc: req.ssrc },
      });
    } catch (err) {
      session.opening.delete(req.trackId);
      this.deps.ports.release(reserved.port);
      if (err instanceof CaptureStartError) throw Errors.captureFailed(err.message);
      throw err;
    }

    const startedAt = Date.parse(manifest.startedAtUtc);
    return this.deps.locks.run(sessionId, async () => {
      session.opening.delete(req.trackId);
      const current = await this.deps.store.readManifest(sessionId);
      if (!current || current.state !== 'capturing' || !this.live.has(sessionId)) {
        await handle.stop(this.deps.settings.stopTimeoutMs);
        this.deps.ports.release(reserved.port);
        throw Errors.sessionClosed();
      }
      current.tracks.push({
        trackId: req.trackId,
        producerId: req.producerId,
        userId: req.userId,
        kind: req.kind,
        source: req.source,
        codec: req.codec,
        ssrc: req.ssrc,
        file,
        rtpPort: reserved.port,
        rtcpPort: reserved.rtcpPort,
        startOffsetMs: Math.max(0, this.now().getTime() - startedAt),
        endedOffsetMs: null,
      });
      await this.save(current);
      session.tracks.set(req.trackId, { handle, rtpPort: reserved.port });
      void handle.exited.then(() => this.onCaptureExit(sessionId, req.trackId, reserved.port));
      this.updateGauges();
      return { created: true, ip: this.deps.settings.announcedIp, port: reserved.port, rtcpPort: reserved.rtcpPort };
    });
  }

  /** Idempotente: pista o sesión desconocida = nada que cerrar. */
  async closeTrack(sessionId: string, trackId: string): Promise<void> {
    const session = this.live.get(sessionId);
    const track = session?.tracks.get(trackId);
    if (!session || !track) return;
    await this.deps.locks.run(sessionId, async () => {
      const manifest = await this.deps.store.readManifest(sessionId);
      if (!manifest) return;
      this.markEnded(manifest, [trackId]);
      await this.save(manifest);
    });
    void track.handle.stop(this.deps.settings.stopTimeoutMs);
  }

  async stopSession(sessionId: string, req: StopSessionRequest): Promise<{ jobId: string }> {
    const outcome = await this.deps.locks.run(sessionId, async () => {
      const manifest = await this.deps.store.readManifest(sessionId);
      if (!manifest) throw Errors.sessionNotFound();
      if (manifest.stop && manifest.jobId) return { jobId: manifest.jobId, session: undefined };
      if (manifest.state !== 'capturing' && manifest.state !== 'limit_reached') throw Errors.sessionClosed();
      const jobId = this.newId();
      manifest.stop = { stoppedAtUtc: req.stoppedAtUtc, reason: req.reason, receivedAtUtc: this.now().toISOString() };
      manifest.jobId = jobId;
      manifest.state = 'queued';
      this.markEnded(manifest);
      await this.deps.store.writeTimeline(sessionId, req.timeline);
      await this.save(manifest);
      const session = this.detach(sessionId);
      return { jobId, session };
    });
    if (outcome.session) {
      const session = outcome.session;
      // Responde ya; las capturas se cierran (trailer WebM) antes de encolar la composición.
      void this.closeCaptures(session)
        .then(() => this.enqueue({ jobId: outcome.jobId, sessionId, mode: 'compose' }))
        .catch((err: unknown) =>
          this.deps.logger.error({ sessionId, err: (err as Error).message }, 'could not enqueue composition'),
        );
      this.deps.logger.info({ sessionId, jobId: outcome.jobId, reason: req.reason }, 'recording session stopped');
    }
    return { jobId: outcome.jobId };
  }

  async recompose(sessionId: string, req: RecomposeRequest): Promise<{ jobId: string }> {
    const jobId = await this.deps.locks.run(sessionId, async () => {
      const manifest = await this.deps.store.readManifest(sessionId);
      if (!manifest || manifest.state === 'aborted') throw Errors.tracksPurged();
      if (manifest.state === 'capturing' || manifest.state === 'limit_reached') throw Errors.sessionActive();
      if (manifest.state === 'queued' || manifest.state === 'composing') throw Errors.compositionInProgress();
      let anyFile = false;
      for (const t of manifest.tracks) {
        if ((await this.deps.store.fileSize(this.deps.store.trackPath(sessionId, t.file))) > 0) anyFile = true;
      }
      if (!anyFile) throw Errors.tracksPurged();
      const id = this.newId();
      manifest.composition = req.composition;
      manifest.jobId = id;
      manifest.state = 'queued';
      delete manifest.failure;
      await this.deps.store.writeTimeline(sessionId, req.timeline);
      await this.save(manifest);
      return id;
    });
    await this.enqueue({ jobId, sessionId, mode: 'recompose' });
    this.deps.logger.info({ sessionId, jobId }, 'recomposition queued');
    return { jobId };
  }

  /** Aborta y descarta (Communication lo usa si la sesión falla antes de componer). */
  async abortSession(sessionId: string): Promise<void> {
    const outcome = await this.deps.locks.run(sessionId, async () => {
      const manifest = await this.deps.store.readManifest(sessionId);
      const session = this.detach(sessionId);
      if (!manifest) return { session, manifest: null, wasQueued: false };
      const wasQueued = manifest.state === 'queued' || manifest.state === 'composing';
      manifest.state = 'aborted';
      manifest.retainUntilUtc = this.now().toISOString();
      this.markEnded(manifest);
      await this.save(manifest);
      return { session, manifest, wasQueued };
    });
    if (outcome.session) await this.closeCaptures(outcome.session);
    if (outcome.manifest && outcome.wasQueued) {
      await this.publishFailure(outcome.manifest, 'Aborted', 'Session aborted by Communication.');
    }
    await this.deps.store.removeSession(sessionId).catch((err: unknown) =>
      // Si ffmpeg aún tiene archivos abiertos (Windows), el janitor la borra después.
      this.deps.logger.warn({ sessionId, err: (err as Error).message }, 'abort: spool not removed yet'),
    );
  }

  /** Arranque: lo que estaba capturando se perdió (RecorderRestarted); lo encolado se reencola. */
  async recoverOnBoot(): Promise<void> {
    const moved = await this.deps.jobs.recover();
    if (moved > 0) this.deps.logger.warn({ moved }, 'compose jobs recovered from processing');
    for (const sessionId of await this.deps.store.listSessionIds()) {
      const manifest = await this.deps.store.readManifest(sessionId).catch(() => null);
      if (!manifest) continue;
      if (manifest.state === 'capturing' || manifest.state === 'limit_reached') {
        manifest.state = 'failed';
        manifest.failure = {
          reason: 'RecorderRestarted',
          message: 'Recorder restarted while capturing.',
          atUtc: this.now().toISOString(),
        };
        manifest.retainUntilUtc = retainUntil(this.now(), manifest.tracksRetentionDays);
        await this.save(manifest);
        await this.publishFailure(manifest, 'RecorderRestarted', 'Recorder restarted while capturing.');
      } else if ((manifest.state === 'queued' || manifest.state === 'composing') && manifest.jobId) {
        if (!(await this.deps.jobs.contains(manifest.jobId))) {
          await this.enqueue({ jobId: manifest.jobId, sessionId, mode: manifest.result ? 'recompose' : 'compose' });
        }
      }
    }
  }

  async shutdown(): Promise<void> {
    const sessions = [...this.live.values()];
    for (const s of sessions) this.clearTimers(s);
    await Promise.all(sessions.map((s) => this.closeCaptures(s)));
  }

  private scheduleHardStop(session: LiveSession, manifest: SessionManifest): void {
    const deadline =
      Date.parse(manifest.startedAtUtc) + (manifest.maxDurationSeconds + manifest.toleranceSeconds) * 1000;
    const delayMs = Math.min(MAX_TIMER_MS, Math.max(0, deadline - this.now().getTime()));
    session.hardStopTimer = setTimeout(() => {
      void this.hardStop(session.sessionId).catch((err: unknown) =>
        this.deps.logger.error({ sessionId: session.sessionId, err: (err as Error).message }, 'hard stop failed'),
      );
    }, delayMs);
    session.hardStopTimer.unref?.();
  }

  /** Corte duro (max + tolerancia): deja de escribir y espera el stop; sin stop, compone igual. */
  async hardStop(sessionId: string): Promise<void> {
    const session = await this.deps.locks.run(sessionId, async () => {
      const manifest = await this.deps.store.readManifest(sessionId);
      const live = this.live.get(sessionId);
      if (!manifest || manifest.state !== 'capturing' || !live) return undefined;
      manifest.state = 'limit_reached';
      manifest.hardStoppedAtUtc = this.now().toISOString();
      this.markEnded(manifest);
      await this.save(manifest);
      return live;
    });
    if (!session) return;
    this.deps.logger.warn({ sessionId }, 'recording hard limit reached; captures closed');
    await this.closeCaptures(session);
    session.orphanTimer = setTimeout(
      () => {
        void this.composeOrphan(sessionId).catch((err: unknown) =>
          this.deps.logger.error({ sessionId, err: (err as Error).message }, 'orphan composition failed'),
        );
      },
      Math.min(MAX_TIMER_MS, this.deps.settings.orphanTimeoutMinutes * 60_000),
    );
    session.orphanTimer.unref?.();
  }

  /** Nunca llegó el stop tras el corte duro: compone con timeline de respaldo ("Participant N"). */
  async composeOrphan(sessionId: string): Promise<void> {
    const jobId = await this.deps.locks.run(sessionId, async () => {
      const manifest = await this.deps.store.readManifest(sessionId);
      if (!manifest || manifest.state !== 'limit_reached') return undefined;
      const id = this.newId();
      manifest.stop = { stoppedAtUtc: null, reason: 'RecorderOrphan', receivedAtUtc: this.now().toISOString() };
      manifest.jobId = id;
      manifest.state = 'queued';
      await this.save(manifest);
      this.detach(sessionId);
      return id;
    });
    if (!jobId) return;
    this.deps.logger.warn({ sessionId, jobId }, 'stop never arrived; composing with fallback timeline');
    await this.enqueue({ jobId, sessionId, mode: 'compose' });
  }

  private onCaptureExit(sessionId: string, trackId: string, rtpPort: number): void {
    this.deps.ports.release(rtpPort);
    const session = this.live.get(sessionId);
    if (session?.tracks.get(trackId)?.rtpPort === rtpPort) session.tracks.delete(trackId);
    this.updateGauges();
    // Salida inesperada (ffmpeg murió): la pista termina aquí.
    void this.deps.locks
      .run(sessionId, async () => {
        const manifest = await this.deps.store.readManifest(sessionId);
        const track = manifest?.tracks.find((t) => t.trackId === trackId);
        if (!manifest || !track || track.endedOffsetMs !== null) return;
        this.markEnded(manifest, [trackId]);
        await this.save(manifest);
        this.deps.logger.warn({ sessionId, trackId }, 'capture process exited unexpectedly');
      })
      .catch(() => undefined);
  }

  private markEnded(manifest: SessionManifest, trackIds?: readonly string[]): void {
    const offset = Math.max(0, this.now().getTime() - Date.parse(manifest.startedAtUtc));
    for (const t of manifest.tracks) {
      if (t.endedOffsetMs === null && (!trackIds || trackIds.includes(t.trackId))) t.endedOffsetMs = offset;
    }
  }

  private detach(sessionId: string): LiveSession | undefined {
    const session = this.live.get(sessionId);
    if (!session) return undefined;
    this.clearTimers(session);
    this.live.delete(sessionId);
    this.updateGauges();
    return session;
  }

  private clearTimers(session: LiveSession): void {
    if (session.hardStopTimer) clearTimeout(session.hardStopTimer);
    if (session.orphanTimer) clearTimeout(session.orphanTimer);
  }

  private async closeCaptures(session: LiveSession): Promise<void> {
    const tracks = [...session.tracks.values()];
    await Promise.all(tracks.map((t) => t.handle.stop(this.deps.settings.stopTimeoutMs).catch(() => undefined)));
  }

  private async enqueue(job: Omit<ComposeJob, 'enqueuedAtUtc'>): Promise<void> {
    await withRetry(() => this.deps.jobs.enqueue({ ...job, enqueuedAtUtc: this.now().toISOString() }), {
      maxAttempts: this.deps.settings.retry.maxAttempts,
      backoffMs: this.deps.settings.retry.backoffMs,
      isRetriable: () => true,
    });
  }

  private async publishFailure(manifest: SessionManifest, reason: FailureReason, message: string): Promise<void> {
    const eventId = this.newId();
    const event = buildCompositionFailedEvent(manifest, {
      eventId,
      occurredOnUtc: this.now().toISOString(),
      reason,
      errorMessage: message,
    });
    try {
      await withRetry(() => this.deps.bus.publishEvent(event.eventType, eventId, event), {
        maxAttempts: this.deps.settings.retry.maxAttempts,
        backoffMs: this.deps.settings.retry.backoffMs,
        isRetriable: () => true,
      });
      this.deps.metrics.failuresTotal.inc({ reason });
    } catch (err) {
      this.deps.logger.error({ sessionId: manifest.sessionId, reason, err: (err as Error).message }, 'could not publish composition_failed');
    }
  }

  private async save(manifest: SessionManifest): Promise<void> {
    manifest.updatedAtUtc = this.now().toISOString();
    await this.deps.store.writeManifest(manifest);
  }

  private updateGauges(): void {
    this.deps.metrics.activeSessions.set(this.activeSessions);
    this.deps.metrics.activeTracks.set(this.activeTracks);
  }
}

