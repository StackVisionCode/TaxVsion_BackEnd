import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import { RecordingSessionState, RecordingScope, isTerminalRecordingState } from './recording-session-state.js';

/**
 * Grabacion de un Meeting o Call — persistida en su propia tabla
 * (RecordingSession, Fase Backend 2), NO como columna de Meeting/Call. Un
 * unico RecordingSession existe por (Scope, ScopeId) durante toda la vida de
 * ese meeting/call (constraint @@unique en Prisma) — no hay "reintentar
 * grabar" dentro de la misma sesion; si falla, queda Failed para siempre.
 *
 * State machine (ver recording-session-state.ts para el diagrama completo):
 *   (sin fila) -> Requesting -> Recording -> Stopping -> Processing -> Ready
 *                      \-> Failed          \-> Failed       \-> Failed
 *
 * Quien orquesta las transiciones es Meeting/Call (ver meeting.ts/call.ts) —
 * esta clase solo protege el state machine en si; la autorizacion de "es
 * host/cohost" vive en el aggregate padre porque RecordingSession no conoce
 * la lista de participantes.
 */
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
}

export class RecordingSession {
  private constructor(private state: RecordingSessionSnapshot) {}

  static rehydrate(snapshot: RecordingSessionSnapshot): RecordingSession {
    return new RecordingSession(snapshot);
  }

  /**
   * Crea una nueva sesion en Requesting. Falla si ya existe una para este
   * (Scope, ScopeId) — el llamador (Meeting.requestRecording/Call.requestRecording)
   * es responsable de cargar la sesion existente (o null) via el repository
   * ANTES de invocar esto, para poder distinguir "no existe" de "existe y
   * viola el @@unique".
   */
  static request(input: {
    tenantId: string;
    scope: RecordingScope;
    scopeId: string;
    requestedByUserId: string;
    now?: Date;
  }): Result<RecordingSession> {
    const snapshot: RecordingSessionSnapshot = {
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
    };
    return Result.ok(new RecordingSession(snapshot));
  }

  /**
   * Transiciona Requesting -> Recording. `policyAllows` es un boolean YA
   * evaluado por el caller (ver evaluateRecordingConsentPolicy en
   * recording-consent.ts) — esta clase no tiene visibilidad de las respuestas
   * de consentimiento individuales (viven en una tabla separada,
   * RecordingConsentEvent), solo protege que la transicion de estado sea
   * valida y que la policy efectivamente haya dado luz verde.
   */
  start(input: { policyAllows: boolean; now?: Date }): Result<void> {
    if (this.state.state !== RecordingSessionState.Requesting) {
      return Result.fail(
        makeError('RecordingSession.InvalidTransition', `Cannot start from ${this.state.state}.`),
      );
    }
    if (!input.policyAllows) {
      return Result.fail(
        makeError('RecordingSession.ConsentPolicyBlocked', 'Recording consent policy does not allow starting.'),
      );
    }
    this.state = { ...this.state, state: RecordingSessionState.Recording, startedAtUtc: input.now ?? new Date() };
    return Result.okVoid();
  }

  /** Transiciona Recording -> Stopping. */
  stop(now?: Date): Result<void> {
    if (this.state.state !== RecordingSessionState.Recording) {
      return Result.fail(
        makeError('RecordingSession.InvalidTransition', `Cannot stop from ${this.state.state}.`),
      );
    }
    this.state = { ...this.state, state: RecordingSessionState.Stopping, stoppedAtUtc: now ?? new Date() };
    return Result.okVoid();
  }

  /**
   * Transiciona Stopping -> Processing. Disparado por attach-meeting-recording.ts
   * (Fase Backend 3) al recibir el fileId ya subido por el cliente — no hay
   * actor que autorizar aca, ya se valido Host/Cohost antes de llegar a este
   * punto (ver Meeting.beginProcessingRecording).
   */
  beginProcessing(): Result<void> {
    if (this.state.state !== RecordingSessionState.Stopping) {
      return Result.fail(
        makeError('RecordingSession.InvalidTransition', `Cannot begin processing from ${this.state.state}.`),
      );
    }
    this.state = { ...this.state, state: RecordingSessionState.Processing };
    return Result.okVoid();
  }

  /**
   * Transiciona a Failed desde cualquier estado no-terminal. Sin restriccion
   * de actor — se dispara tanto por el host (cancela la solicitud) como por
   * el sistema (transcript worker publica TranscriptFailed, Fase Backend 3).
   */
  fail(input: { reason: string; now?: Date }): Result<void> {
    if (isTerminalRecordingState(this.state.state)) {
      return Result.fail(
        makeError('RecordingSession.InvalidTransition', `Cannot fail a terminal session (${this.state.state}).`),
      );
    }
    this.state = {
      ...this.state,
      state: RecordingSessionState.Failed,
      failureReason: input.reason,
    };
    return Result.okVoid();
  }

  /**
   * Transiciona Processing -> Ready. Fase Backend 2 no expone ninguna forma
   * de llegar a Processing (eso lo hace attachRecording en Fase Backend 3) —
   * en esta fase se prueba en aislamiento rehidratando una sesion en
   * Processing directamente.
   */
  complete(input: { recordingFileId: string; durationSeconds: number; now?: Date }): Result<void> {
    if (this.state.state !== RecordingSessionState.Processing) {
      return Result.fail(
        makeError('RecordingSession.InvalidTransition', `Cannot complete from ${this.state.state}.`),
      );
    }
    if (input.durationSeconds < 0) {
      return Result.fail(makeError('RecordingSession.InvalidDuration', 'durationSeconds cannot be negative.'));
    }
    this.state = {
      ...this.state,
      state: RecordingSessionState.Ready,
      recordingFileId: input.recordingFileId,
      durationSeconds: input.durationSeconds,
    };
    return Result.okVoid();
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
}
