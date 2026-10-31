import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import {
  RecordingCaptureMode,
  RecordingSessionState,
  RecordingStopReason,
  type RecordingScope,
  isTerminalRecordingState,
} from './recording-session-state.js';

/**
 * Grabacion de un Meeting o Call — tabla propia (RecordingSession), NO columna de Meeting/Call. Una
 * sola sesion por (Scope, ScopeId) durante toda la vida del meeting/call; si falla, queda Failed.
 *
 * Quien orquesta las transiciones es Meeting/Call (autorizacion host/cohost); esta clase protege el
 * state machine (ver recording-session-state.ts) y los datos de la captura server-side de F7.
 */

/** F7: pista de audio subida por el recorder (las de video quedan en su spool). */
export interface RecordingAudioTrack {
  readonly trackId: string;
  readonly userId: string;
  readonly fileId: string;
  readonly source: 'camera' | 'screen';
  readonly startOffsetMs: number;
  readonly durationMs: number;
}

export interface RecordingTracksManifest {
  readonly audioTracks: readonly RecordingAudioTrack[];
  readonly videoTrackCount: number;
}

export interface RecordingSessionSnapshot {
  readonly id: string;
  readonly tenantId: string;
  readonly scope: RecordingScope;
  readonly scopeId: string;
  readonly state: RecordingSessionState;
  readonly requestedByUserId: string;
  readonly requestedAtUtc: Date;
  readonly startedAtUtc: Date | null;
  readonly stoppedAtUtc: Date | null;
  readonly recordingFileId: string | null;
  readonly durationSeconds: number | null;
  readonly failureReason: string | null;
  // F7
  readonly captureMode: RecordingCaptureMode;
  readonly maxDurationSeconds: number | null;
  readonly limitWarnedAtUtc: Date | null;
  readonly stopReason: RecordingStopReason | null;
  readonly composedFileId: string | null;
  readonly composedSizeBytes: number | null;
  readonly composedFormat: string | null;
  readonly tracksManifest: RecordingTracksManifest | null;
  readonly composedAtUtc: Date | null;
  readonly readyAtUtc: Date | null;
  readonly tracksPurgedAtUtc: Date | null;
  readonly purgedAtUtc: Date | null;
}

/**
 * Fallos de la composición (las pistas siguen en el spool del recorder): PlatformAdmin puede recomponer.
 * Los de captura o almacenamiento no se rescatan así.
 */
const RECOMPOSABLE_FAILURES: ReadonlySet<string> = new Set(['ComposeError', 'CompositionTimeout', 'UploadFailed', 'RecorderRestarted']);

const invalid = (from: string, action: string) =>
  makeError('RecordingSession.InvalidTransition', `Cannot ${action} from ${from}.`);

export class RecordingSession {
  private constructor(private state: RecordingSessionSnapshot) {}

  static rehydrate(snapshot: RecordingSessionSnapshot): RecordingSession {
    return new RecordingSession(snapshot);
  }

  /**
   * Crea una nueva sesion en Requesting. El llamador (Meeting/Call.requestRecording) carga antes la
   * existente para distinguir "no existe" de "existe y viola el @@unique".
   */
  static request(input: {
    tenantId: string;
    scope: RecordingScope;
    scopeId: string;
    requestedByUserId: string;
    now?: Date;
  }): Result<RecordingSession> {
    return Result.ok(
      new RecordingSession({
        id: randomUUID(),
        tenantId: input.tenantId,
        scope: input.scope,
        scopeId: input.scopeId,
        state: RecordingSessionState.Requesting,
        requestedByUserId: input.requestedByUserId,
        requestedAtUtc: input.now ?? new Date(),
        startedAtUtc: null,
        stoppedAtUtc: null,
        recordingFileId: null,
        durationSeconds: null,
        failureReason: null,
        captureMode: RecordingCaptureMode.Client,
        maxDurationSeconds: null,
        limitWarnedAtUtc: null,
        stopReason: null,
        composedFileId: null,
        composedSizeBytes: null,
        composedFormat: null,
        tracksManifest: null,
        composedAtUtc: null,
        readyAtUtc: null,
        tracksPurgedAtUtc: null,
        purgedAtUtc: null,
      }),
    );
  }

  /**
   * Requesting -> Recording. `policyAllows` ya evaluado por el caller (consentimiento). F7: fija el
   * modo de captura y el limite efectivo (T-35); `maxDurationSeconds` null = sin limite.
   */
  start(input: {
    policyAllows: boolean;
    now?: Date;
    captureMode?: RecordingCaptureMode;
    maxDurationSeconds?: number | null;
  }): Result<void> {
    if (this.state.state !== RecordingSessionState.Requesting) return Result.fail(invalid(this.state.state, 'start'));
    if (!input.policyAllows) {
      return Result.fail(
        makeError('RecordingSession.ConsentPolicyBlocked', 'Recording consent policy does not allow starting.'),
      );
    }
    const max = input.maxDurationSeconds ?? null;
    if (max !== null && (!Number.isInteger(max) || max <= 0)) {
      return Result.fail(makeError('RecordingSession.InvalidLimit', 'maxDurationSeconds must be a positive integer.'));
    }
    this.state = {
      ...this.state,
      state: RecordingSessionState.Recording,
      startedAtUtc: input.now ?? new Date(),
      captureMode: input.captureMode ?? this.state.captureMode,
      maxDurationSeconds: max,
    };
    return Result.okVoid();
  }

  /** Instante en que vence el limite (null si no hay limite o no arranco). */
  limitAtUtc(): Date | null {
    const { startedAtUtc, maxDurationSeconds } = this.state;
    if (!startedAtUtc || maxDurationSeconds === null) return null;
    return new Date(startedAtUtc.getTime() + maxDurationSeconds * 1000);
  }

  /** T-35: el aviso previo se da una sola vez, solo grabando y solo si hay limite. */
  shouldWarnLimit(now: Date, warningSeconds: number): boolean {
    const limit = this.limitAtUtc();
    if (this.state.state !== RecordingSessionState.Recording || !limit || warningSeconds <= 0) return false;
    if (this.state.limitWarnedAtUtc !== null) return false;
    return now.getTime() >= limit.getTime() - warningSeconds * 1000 && now.getTime() < limit.getTime();
  }

  warnLimit(now: Date): Result<void> {
    if (this.state.state !== RecordingSessionState.Recording) return Result.fail(invalid(this.state.state, 'warn'));
    if (this.state.limitWarnedAtUtc !== null) return Result.okVoid();
    this.state = { ...this.state, limitWarnedAtUtc: now };
    return Result.okVoid();
  }

  isLimitReached(now: Date): boolean {
    const limit = this.limitAtUtc();
    return this.state.state === RecordingSessionState.Recording && limit !== null && now.getTime() >= limit.getTime();
  }

  /** Recording -> Stopping. */
  stop(now?: Date, reason: RecordingStopReason = RecordingStopReason.User): Result<void> {
    if (this.state.state !== RecordingSessionState.Recording) return Result.fail(invalid(this.state.state, 'stop'));
    this.state = { ...this.state, state: RecordingSessionState.Stopping, stoppedAtUtc: now ?? new Date(), stopReason: reason };
    return Result.okVoid();
  }

  /** Server: Stopping -> Composing (el recorder acepto el job de composicion). */
  beginComposing(): Result<void> {
    if (this.state.state !== RecordingSessionState.Stopping) return Result.fail(invalid(this.state.state, 'begin composing'));
    if (this.state.captureMode !== RecordingCaptureMode.Server) {
      return Result.fail(makeError('RecordingSession.NotServerCapture', 'Only server-side recordings are composed.'));
    }
    this.state = { ...this.state, state: RecordingSessionState.Composing };
    return Result.okVoid();
  }

  /** Server: Composing -> Processing con el compuesto ya subido (falta que CloudStorage lo marque Available). */
  composed(input: {
    composedFileId: string;
    composedSizeBytes: number;
    format: string;
    durationSeconds: number;
    manifest: RecordingTracksManifest;
    now?: Date;
  }): Result<void> {
    if (this.state.state !== RecordingSessionState.Composing) {
      return Result.fail(invalid(this.state.state, 'attach the composed file'));
    }
    if (input.durationSeconds < 0 || input.composedSizeBytes < 0) {
      return Result.fail(makeError('RecordingSession.InvalidComposition', 'Composed file metadata cannot be negative.'));
    }
    this.state = {
      ...this.state,
      state: RecordingSessionState.Processing,
      composedFileId: input.composedFileId,
      recordingFileId: input.composedFileId,
      composedSizeBytes: input.composedSizeBytes,
      composedFormat: input.format,
      durationSeconds: Math.round(input.durationSeconds),
      tracksManifest: input.manifest,
      composedAtUtc: input.now ?? new Date(),
      readyAtUtc: null,
      failureReason: null,
    };
    return Result.okVoid();
  }

  /** Recompose (PlatformOnly): Ready -> Composing; el compuesto vigente sigue hasta que llegue el nuevo. */
  beginRecomposing(): Result<void> {
    const rescuable =
      this.state.state === RecordingSessionState.Failed && RECOMPOSABLE_FAILURES.has(this.state.failureReason ?? '');
    if ((this.state.state !== RecordingSessionState.Ready && !rescuable) || this.state.captureMode !== RecordingCaptureMode.Server) {
      return Result.fail(invalid(this.state.state, 'recompose'));
    }
    if (this.state.purgedAtUtc !== null) {
      return Result.fail(makeError('Recording.TracksPurged', 'The recording was already purged.'));
    }
    this.state = { ...this.state, state: RecordingSessionState.Composing };
    return Result.okVoid();
  }

  /** Client (fallback): Stopping -> Processing al adjuntar el archivo subido por el navegador. */
  beginProcessing(recordingFileId?: string): Result<void> {
    if (this.state.state !== RecordingSessionState.Stopping) return Result.fail(invalid(this.state.state, 'begin processing'));
    if (this.state.captureMode !== RecordingCaptureMode.Client) {
      return Result.fail(makeError('RecordingSession.ServerCapture', 'Server-side recordings are not uploaded by the client.'));
    }
    this.state = {
      ...this.state,
      state: RecordingSessionState.Processing,
      recordingFileId: recordingFileId ?? this.state.recordingFileId,
    };
    return Result.okVoid();
  }

  /** Processing -> Ready cuando el archivo de la grabacion ya esta Available (T-34). */
  markReady(input: { now?: Date } = {}): Result<void> {
    if (this.state.state !== RecordingSessionState.Processing) return Result.fail(invalid(this.state.state, 'mark ready'));
    if (!this.state.recordingFileId) {
      return Result.fail(makeError('RecordingSession.NoFile', 'A recording file is required before Ready.'));
    }
    const fallbackDuration =
      this.state.startedAtUtc && this.state.stoppedAtUtc
        ? Math.max(0, Math.round((this.state.stoppedAtUtc.getTime() - this.state.startedAtUtc.getTime()) / 1000))
        : 0;
    this.state = {
      ...this.state,
      state: RecordingSessionState.Ready,
      durationSeconds: this.state.durationSeconds ?? fallbackDuration,
      readyAtUtc: input.now ?? new Date(),
    };
    return Result.okVoid();
  }

  /** Cualquier estado no terminal -> Failed (host cancela, recorder/CloudStorage fallan). */
  fail(input: { reason: string; now?: Date }): Result<void> {
    if (isTerminalRecordingState(this.state.state)) {
      return Result.fail(makeError('RecordingSession.InvalidTransition', `Cannot fail a terminal session (${this.state.state}).`));
    }
    this.state = { ...this.state, state: RecordingSessionState.Failed, failureReason: input.reason.slice(0, 500) };
    return Result.okVoid();
  }

  /** T-37: las pistas de audio se pidieron borrar (el compuesto sigue). */
  markTracksPurged(now: Date): void {
    this.state = { ...this.state, tracksPurgedAtUtc: this.state.tracksPurgedAtUtc ?? now };
  }

  /** T-37: retencion de la oficina vencida; ya no hay archivo que ofrecer. */
  markPurged(now: Date): void {
    this.state = {
      ...this.state,
      purgedAtUtc: this.state.purgedAtUtc ?? now,
      tracksPurgedAtUtc: this.state.tracksPurgedAtUtc ?? now,
    };
  }

  toSnapshot(): RecordingSessionSnapshot {
    return this.state;
  }

  get id(): string {
    return this.state.id;
  }
  get scope(): RecordingScope {
    return this.state.scope;
  }
  get scopeId(): string {
    return this.state.scopeId;
  }
  get sessionState(): RecordingSessionState {
    return this.state.state;
  }
  get requestedByUserId(): string {
    return this.state.requestedByUserId;
  }
  get captureMode(): RecordingCaptureMode {
    return this.state.captureMode;
  }
}
