import { resolveAvatars, type AvatarResolver } from '../use-cases/resolve-avatars.js';
import { randomUUID } from 'node:crypto';
import { logger } from '../../infrastructure/logger/logger.js';
import { Result, makeError } from '../../domain/shared/result.js';
import type { RecordingSession } from '../../domain/recording/recording-session.js';
import type { RecordingCompositionPolicy } from '../../domain/settings/effective-communication-settings.js';
import { MeetingEventScope } from '../../domain/meetings/meeting-event-log.js';
import type { SfuProducerLifecycle, SfuService } from '../ports/sfu-service.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { MeetingEventLogRepository } from '../ports/meeting-event-log-repository.js';
import {
  RecorderRequestError,
  type RecorderClient,
  type RecorderTimeline,
  type RecorderTimelineEvent,
  type RecorderTimelineParticipant,
} from '../ports/recorder-client.js';

/** Lo que el recorder necesita saber de una sesion (sale de los efectivos de la oficina, nunca de constantes). */
export interface RecordingCaptureContext {
  readonly captureVideo: boolean;
  readonly maxDurationSeconds: number;
  readonly toleranceSeconds: number;
  readonly tracksRetentionDays: number;
  readonly composition: RecordingCompositionPolicy;
}

export interface RecordingCaptureDeps {
  readonly recorder: RecorderClient;
  readonly sfu: SfuService;
  readonly recordingSessions: RecordingSessionRepository;
  readonly meetings: MeetingRepository;
  readonly calls: CallRepository;
  readonly meetingEvents?: MeetingEventLogRepository | undefined;
  readonly resolveContext: (session: RecordingSession) => Promise<RecordingCaptureContext>;
  /** Fotos de perfil para el slate de cámara apagada; sin resolver o si falla, el video sale con iniciales. */
  readonly avatars?: AvatarResolver | undefined;
  readonly tapListenIp: string;
  readonly keyframeIntervalSeconds: number;
}

interface CapturedTrack {
  readonly trackId: string;
  readonly tapId: string;
  readonly kind: 'audio' | 'video';
}

interface ActiveCapture {
  readonly tenantId: string;
  readonly session: RecordingSession;
  readonly context: RecordingCaptureContext;
  readonly tracks: Map<string, CapturedTrack>;
  keyframeTimer: NodeJS.Timeout | null;
}

/** Tope de filas del MeetingEventLog que viajan en el timeline (una reunion normal tiene cientos). */
const TIMELINE_PAGE = 500;
const TIMELINE_MAX_EVENTS = 20_000;

/**
 * F7 (D-28): captura server-side por pista. Un tap (PlainTransport + consumer) por producer del router
 * hacia el recorder, mientras la sesion este Recording con `captureMode=Server`. El router se identifica
 * por el id del meeting o de la llamada (las llamadas grabadas publican a un router propio, D-56).
 *
 * Estado en memoria por proceso (igual que el SFU): si el proceso reinicia, los producers tambien
 * mueren y, al volver a producir, `onProducerOpened` recupera la captura leyendo la sesion de la BD.
 */
export class RecordingCaptureService {
  private readonly active = new Map<string, ActiveCapture>();
  private hostAddress: string | null = null;

  constructor(private readonly deps: RecordingCaptureDeps) {}

  get available(): boolean {
    return this.deps.recorder.configured;
  }

  /** Abre la sesion en el recorder y engancha los producers que ya existen. */
  async begin(session: RecordingSession): Promise<Result<void>> {
    const snap = session.toSnapshot();
    if (!this.available) {
      return Result.fail(makeError('Recording.RecorderUnavailable', 'The recording service is not available.'));
    }
    const context = await this.deps.resolveContext(session);
    try {
      await this.deps.recorder.startSession(snap.tenantId, {
        sessionId: snap.id,
        tenantId: snap.tenantId,
        scope: snap.scope,
        scopeId: snap.scopeId,
        startedAtUtc: (snap.startedAtUtc ?? new Date()).toISOString(),
        maxDurationSeconds: context.maxDurationSeconds,
        toleranceSeconds: context.toleranceSeconds,
        tracksRetentionDays: context.tracksRetentionDays,
        captureVideo: context.captureVideo,
        composition: compositionForRecorder(context.composition),
      });
    } catch (err) {
      logger.error({ err, sessionId: snap.id }, 'recorder startSession failed');
      return Result.fail(makeError('Recording.RecorderUnavailable', 'The recording service is not available.'));
    }
    const capture = this.register(snap.tenantId, session, context);
    for (const producer of this.deps.sfu.listAllProducers(snap.scopeId)) {
      await this.attach(capture, { ...producer, tenantId: snap.tenantId, meetingId: snap.scopeId });
    }
    return Result.okVoid();
  }

  async onProducerOpened(event: SfuProducerLifecycle): Promise<void> {
    const capture = this.active.get(event.meetingId) ?? (await this.recover(event));
    if (capture) await this.attach(capture, event);
  }

  async onProducerClosed(event: SfuProducerLifecycle): Promise<void> {
    const capture = this.active.get(event.meetingId);
    const track = capture?.tracks.get(event.producerId);
    if (!capture || !track) return;
    capture.tracks.delete(event.producerId);
    await this.deps.sfu.closeRecordingTap({ meetingId: event.meetingId, tapId: track.tapId }).catch(() => undefined);
    await this.deps.recorder
      .closeTrack(capture.tenantId, capture.session.id, track.trackId)
      .catch((err: unknown) => logger.warn({ err, sessionId: capture.session.id, trackId: track.trackId }, 'recorder closeTrack failed'));
  }

  /** Suelta los taps y pide componer con el timeline. Falla = la sesion no puede componerse. */
  async stop(session: RecordingSession, reason: string): Promise<Result<void>> {
    const snap = session.toSnapshot();
    this.release(snap.scopeId);
    try {
      const timeline = await this.buildTimeline(session);
      await this.deps.recorder.stopSession(snap.tenantId, snap.id, {
        stoppedAtUtc: (snap.stoppedAtUtc ?? new Date()).toISOString(),
        reason,
        timeline,
      });
      return Result.okVoid();
    } catch (err) {
      logger.error({ err, sessionId: snap.id }, 'recorder stopSession failed');
      const code = err instanceof RecorderRequestError ? err.code : 'Recording.RecorderUnavailable';
      return Result.fail(makeError(code, 'The recording could not be finalized.'));
    }
  }

  /** La sesion fallo antes de componer: taps fuera y spool descartado. */
  async abort(session: RecordingSession): Promise<void> {
    const snap = session.toSnapshot();
    this.release(snap.scopeId);
    if (!this.available) return;
    await this.deps.recorder
      .abortSession(snap.tenantId, snap.id)
      .catch((err: unknown) => logger.warn({ err, sessionId: snap.id }, 'recorder abortSession failed'));
  }

  /** PlatformOnly: recomponer con la politica vigente a partir del spool del recorder. */
  async recompose(session: RecordingSession): Promise<Result<void>> {
    const snap = session.toSnapshot();
    if (!this.available) return Result.fail(makeError('Recording.RecorderUnavailable', 'The recording service is not available.'));
    const context = await this.deps.resolveContext(session);
    try {
      await this.deps.recorder.recompose(snap.tenantId, snap.id, {
        timeline: await this.buildTimeline(session),
        composition: compositionForRecorder(context.composition),
      });
      return Result.okVoid();
    } catch (err) {
      const code = err instanceof RecorderRequestError ? err.code : 'Recording.RecorderUnavailable';
      return Result.fail(makeError(code, err instanceof Error ? err.message : 'Recompose failed.'));
    }
  }

  isCapturing(routerKey: string): boolean {
    return this.active.has(routerKey);
  }

  private register(tenantId: string, session: RecordingSession, context: RecordingCaptureContext): ActiveCapture {
    const routerKey = session.scopeId;
    this.release(routerKey);
    const capture: ActiveCapture = { tenantId, session, context, tracks: new Map(), keyframeTimer: null };
    if (this.deps.keyframeIntervalSeconds > 0) {
      // Keyframes periodicos: si el recorder pierde un paquete de un keyframe, la pista se recupera sola.
      capture.keyframeTimer = setInterval(() => {
        for (const track of capture.tracks.values()) {
          if (track.kind === 'video') void this.deps.sfu.requestRecordingTapKeyFrame({ meetingId: routerKey, tapId: track.tapId });
        }
      }, this.deps.keyframeIntervalSeconds * 1000);
      capture.keyframeTimer.unref?.();
    }
    this.active.set(routerKey, capture);
    return capture;
  }

  private release(routerKey: string): void {
    const capture = this.active.get(routerKey);
    if (!capture) return;
    if (capture.keyframeTimer) clearInterval(capture.keyframeTimer);
    this.active.delete(routerKey);
    for (const track of capture.tracks.values()) {
      void this.deps.sfu.closeRecordingTap({ meetingId: routerKey, tapId: track.tapId }).catch(() => undefined);
    }
    capture.tracks.clear();
  }

  /** Tras un reinicio: la sesion sigue Recording/Server en BD; se reabre en el recorder (idempotente). */
  private async recover(event: SfuProducerLifecycle): Promise<ActiveCapture | null> {
    if (!event.tenantId || !this.available) return null;
    const session =
      (await this.deps.recordingSessions.findByScope(event.tenantId, 'Meeting', event.meetingId)) ??
      (await this.deps.recordingSessions.findByScope(event.tenantId, 'Call', event.meetingId));
    if (!session || session.sessionState !== 'Recording' || session.captureMode !== 'Server') return null;
    const begun = await this.begin(session);
    return begun.isSuccess ? (this.active.get(event.meetingId) ?? null) : null;
  }

  private async attach(capture: ActiveCapture, producer: SfuProducerLifecycle): Promise<void> {
    if (capture.tracks.has(producer.producerId)) return;
    if (producer.kind === 'video' && !capture.context.captureVideo) return;
    const routerKey = capture.session.scopeId;
    let tapId: string | null = null;
    try {
      const tap = await this.deps.sfu.prepareRecordingTap({
        meetingId: routerKey,
        producerId: producer.producerId,
        listenIp: this.deps.tapListenIp,
        preferredSpatialLayer: capture.context.composition.captureSpatialLayer,
      });
      if (!tap) return;
      tapId = tap.tapId;
      const trackId = randomUUID();
      const endpoint = await this.deps.recorder.addTrack(capture.tenantId, capture.session.id, {
        trackId,
        producerId: producer.producerId,
        userId: producer.userId,
        kind: tap.kind,
        source: producer.source,
        codec: tap.codec,
        ssrc: tap.ssrc,
      });
      const ip = endpoint.ip ?? (await this.recorderAddress());
      const connected = await this.deps.sfu.connectRecordingTap({ meetingId: routerKey, tapId, ip, port: endpoint.port, rtcpPort: endpoint.rtcpPort });
      if (!connected) throw new Error('Recording tap vanished before connecting.');
      capture.tracks.set(producer.producerId, { trackId, tapId, kind: tap.kind });
    } catch (err) {
      // Una pista que no se pudo enganchar no tumba la grabacion: se registra y sigue el resto.
      logger.warn({ err, sessionId: capture.session.id, producerId: producer.producerId }, 'recording track attach failed');
      if (tapId) await this.deps.sfu.closeRecordingTap({ meetingId: routerKey, tapId }).catch(() => undefined);
    }
  }

  private async recorderAddress(): Promise<string> {
    this.hostAddress ??= await this.deps.recorder.resolveHostAddress();
    return this.hostAddress;
  }

  /** Participantes con nombre y rol + todas las filas del MeetingEventLog hasta el corte (el recorder deriva el estado inicial). */
  async buildTimeline(session: RecordingSession): Promise<RecorderTimeline> {
    const snap = session.toSnapshot();
    const participants: RecorderTimelineParticipant[] = [];
    if (snap.scope === 'Meeting') {
      const meeting = await this.deps.meetings.findById(snap.tenantId, snap.scopeId);
      for (const p of meeting?.toSnapshot().participants ?? []) {
        participants.push({ userId: p.userId, displayName: p.displayName, role: p.role, actorType: p.actorType ?? null, avatarFileId: null });
      }
    } else {
      const call = await this.deps.calls.findById(snap.tenantId, snap.scopeId);
      for (const p of call?.toSnapshot().participants ?? []) {
        participants.push({ userId: p.userId, displayName: p.displayName, role: 'Attendee', actorType: null, avatarFileId: null });
      }
    }
    const photos = await resolveAvatars(this.deps.avatars, participants.map((p) => p.userId));
    const withPhotos = photos ? participants.map((p) => ({ ...p, avatarFileId: photos.of(p.userId) })) : participants;
    const events: RecorderTimelineEvent[] = [];
    if (this.deps.meetingEvents) {
      const until = (snap.stoppedAtUtc ?? new Date()).getTime();
      let afterSeq = 0;
      for (;;) {
        const page = await this.deps.meetingEvents.list({
          tenantId: snap.tenantId,
          scope: snap.scope === 'Meeting' ? MeetingEventScope.Meeting : MeetingEventScope.Call,
          scopeId: snap.scopeId,
          afterSeq,
          take: TIMELINE_PAGE,
        });
        for (const e of page) {
          if (e.at.getTime() <= until) {
            events.push({
              seq: e.seq,
              at: e.at.toISOString(),
              type: e.type,
              actorUserId: e.actorUserId,
              targetUserId: e.targetUserId,
              payload: e.payload,
            });
          }
        }
        const last = page.at(-1);
        if (!last || page.length < TIMELINE_PAGE || events.length >= TIMELINE_MAX_EVENTS) break;
        afterSeq = last.seq;
      }
    }
    return { participants: withPhotos, events };
  }
}

function compositionForRecorder(c: RecordingCompositionPolicy): Omit<RecordingCompositionPolicy, 'captureSpatialLayer'> {
  return { width: c.width, height: c.height, fps: c.fps, videoKbps: c.videoKbps, audioKbps: c.audioKbps, format: c.format };
}
