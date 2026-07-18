import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import { CallKind } from './call-kind.js';
import { CallStatus, isTerminal } from './call-status.js';
import { CallParticipant, type CallParticipantSnapshot } from './call-participant.js';
import type { ConnectionQuality, MediaStatus } from './media-status.js';
import { RecordingSession } from '../recording/recording-session.js';
import { RecordingScope } from '../recording/recording-session-state.js';
import { RecordingConsentEntry } from '../recording/recording-consent-entry.js';
import type { RecordingConsentEntryStatus } from '../recording/recording-consent.js';

export type CallEndReason = 'Hangup' | 'Missed' | 'Rejected' | 'Cancelled' | 'IceFailed';

export interface CallSnapshot {
  readonly id: string;
  readonly tenantId: string;
  readonly kind: CallKind;
  readonly status: CallStatus;
  readonly callerUserId: string;
  readonly calleeUserId: string;
  readonly conversationId: string | null;
  readonly ringingAtUtc: Date;
  readonly acceptedAtUtc: Date | null;
  readonly startedAtUtc: Date | null;
  readonly endedAtUtc: Date | null;
  readonly endReason: CallEndReason | null;
  readonly durationSeconds: number | null;
  readonly recordingRequested: boolean;
  readonly recordingFileId: string | null;
  readonly transcriptFileId: string | null;
  readonly createdAtUtc: Date;
  readonly updatedAtUtc: Date;
  readonly participants: readonly CallParticipantSnapshot[];
}

export class Call {
  private state: CallSnapshot;
  private participants: CallParticipant[];

  private constructor(snapshot: CallSnapshot) {
    this.state = snapshot;
    this.participants = snapshot.participants.map(CallParticipant.rehydrate);
  }

  static rehydrate(snapshot: CallSnapshot): Call {
    return new Call(snapshot);
  }

  /**
   * Inicia una llamada. El caller entra automaticamente como participante con
   * JoinOrder=1; el callee se une al aceptar (JoinOrder=2). Regla del plan:
   * el que joinOrder MAYOR es el peer "polite" en perfect negotiation.
   */
  static initiate(input: {
    tenantId: string;
    kind: CallKind;
    caller: { userId: string; displayName: string };
    callee: { userId: string; displayName: string };
    conversationId?: string | null;
    recordingRequested?: boolean;
    now?: Date;
  }): Result<Call> {
    if (input.caller.userId === input.callee.userId) {
      return Result.fail(makeError('Call.SelfCall', 'Cannot call yourself.'));
    }
    const now = input.now ?? new Date();
    const callId = randomUUID();

    const callerParticipant = CallParticipant.create({
      callId,
      tenantId: input.tenantId,
      userId: input.caller.userId,
      displayName: input.caller.displayName,
      role: 'Caller',
      joinOrder: 1,
      audioDefault: true,
      videoDefault: input.kind === 'Video',
      now,
    });

    const snapshot: CallSnapshot = {
      id: callId,
      tenantId: input.tenantId,
      kind: input.kind,
      status: CallStatus.Ringing,
      callerUserId: input.caller.userId,
      calleeUserId: input.callee.userId,
      conversationId: input.conversationId ?? null,
      ringingAtUtc: now,
      acceptedAtUtc: null,
      startedAtUtc: null,
      endedAtUtc: null,
      endReason: null,
      durationSeconds: null,
      recordingRequested: input.recordingRequested ?? false,
      recordingFileId: null,
      transcriptFileId: null,
      createdAtUtc: now,
      updatedAtUtc: now,
      participants: [callerParticipant.toSnapshot()],
    };
    return Result.ok(new Call(snapshot));
  }

  accept(input: { byUserId: string; calleeDisplayName: string; now?: Date }): Result<void> {
    if (this.state.status !== CallStatus.Ringing) {
      return Result.fail(makeError('Call.InvalidTransition', `Cannot accept a call in status ${this.state.status}.`));
    }
    if (input.byUserId !== this.state.calleeUserId) {
      return Result.fail(makeError('Call.AcceptForbidden', 'Only the callee can accept the call.'));
    }
    const now = input.now ?? new Date();
    const calleeParticipant = CallParticipant.create({
      callId: this.state.id,
      tenantId: this.state.tenantId,
      userId: this.state.calleeUserId,
      displayName: input.calleeDisplayName,
      role: 'Callee',
      joinOrder: 2,
      audioDefault: true,
      videoDefault: this.state.kind === 'Video',
      now,
    });
    this.participants.push(calleeParticipant);
    this.state = {
      ...this.state,
      status: CallStatus.Accepted,
      acceptedAtUtc: now,
      updatedAtUtc: now,
      participants: this.participants.map((p) => p.toSnapshot()),
    };
    return Result.okVoid();
  }

  markActive(now: Date = new Date()): Result<void> {
    if (this.state.status !== CallStatus.Accepted) {
      return Result.fail(makeError('Call.InvalidTransition', `Cannot mark active from ${this.state.status}.`));
    }
    this.state = {
      ...this.state,
      status: CallStatus.Active,
      startedAtUtc: now,
      updatedAtUtc: now,
    };
    return Result.okVoid();
  }

  reject(input: { byUserId: string; now?: Date }): Result<void> {
    if (this.state.status !== CallStatus.Ringing) {
      return Result.fail(makeError('Call.InvalidTransition', `Cannot reject a call in ${this.state.status}.`));
    }
    if (input.byUserId !== this.state.calleeUserId) {
      return Result.fail(makeError('Call.RejectForbidden', 'Only the callee can reject the call.'));
    }
    const now = input.now ?? new Date();
    this.state = {
      ...this.state,
      status: CallStatus.Rejected,
      endedAtUtc: now,
      endReason: 'Rejected',
      durationSeconds: 0,
      updatedAtUtc: now,
    };
    return Result.okVoid();
  }

  cancel(input: { byUserId: string; now?: Date }): Result<void> {
    if (this.state.status !== CallStatus.Ringing) {
      return Result.fail(makeError('Call.InvalidTransition', `Cannot cancel a call in ${this.state.status}.`));
    }
    if (input.byUserId !== this.state.callerUserId) {
      return Result.fail(makeError('Call.CancelForbidden', 'Only the caller can cancel the call.'));
    }
    const now = input.now ?? new Date();
    this.state = {
      ...this.state,
      status: CallStatus.Cancelled,
      endedAtUtc: now,
      endReason: 'Cancelled',
      durationSeconds: 0,
      updatedAtUtc: now,
    };
    return Result.okVoid();
  }

  markMissed(now: Date = new Date()): Result<void> {
    if (this.state.status !== CallStatus.Ringing) {
      return Result.fail(makeError('Call.InvalidTransition', `Cannot mark missed from ${this.state.status}.`));
    }
    this.state = {
      ...this.state,
      status: CallStatus.MissedCall,
      endedAtUtc: now,
      endReason: 'Missed',
      durationSeconds: 0,
      updatedAtUtc: now,
    };
    return Result.okVoid();
  }

  end(input: { byUserId: string; reason?: CallEndReason; now?: Date }): Result<void> {
    if (
      this.state.status !== CallStatus.Accepted &&
      this.state.status !== CallStatus.Active
    ) {
      return Result.fail(makeError('Call.InvalidTransition', `Cannot end a call in ${this.state.status}.`));
    }
    if (input.byUserId !== this.state.callerUserId && input.byUserId !== this.state.calleeUserId) {
      return Result.fail(makeError('Call.EndForbidden', 'Only caller or callee can end the call.'));
    }
    const now = input.now ?? new Date();
    const start = this.state.startedAtUtc ?? this.state.acceptedAtUtc ?? this.state.ringingAtUtc;
    const durationSeconds = Math.max(0, Math.floor((now.getTime() - start.getTime()) / 1000));

    this.participants.forEach((p) => {
      if (!p.hasLeft) p.markLeft(now);
    });

    this.state = {
      ...this.state,
      status: CallStatus.Ended,
      endedAtUtc: now,
      endReason: input.reason ?? 'Hangup',
      durationSeconds,
      updatedAtUtc: now,
      participants: this.participants.map((p) => p.toSnapshot()),
    };
    return Result.okVoid();
  }

  fail(input: { reason: CallEndReason; now?: Date }): Result<void> {
    if (isTerminal(this.state.status)) {
      return Result.fail(makeError('Call.InvalidTransition', `Cannot fail a terminal call (${this.state.status}).`));
    }
    const now = input.now ?? new Date();
    this.participants.forEach((p) => {
      if (!p.hasLeft) p.markLeft(now);
    });
    this.state = {
      ...this.state,
      status: CallStatus.Failed,
      endedAtUtc: now,
      endReason: input.reason,
      durationSeconds:
        this.state.startedAtUtc
          ? Math.max(0, Math.floor((now.getTime() - this.state.startedAtUtc.getTime()) / 1000))
          : 0,
      updatedAtUtc: now,
      participants: this.participants.map((p) => p.toSnapshot()),
    };
    return Result.okVoid();
  }

  /**
   * Fase Backend 7 — upgrade audio→video. Solo valido si la llamada esta
   * `Active` y es una llamada de audio (no downgrade video→audio, no upgrade
   * antes de aceptar). El signaling WebRTC lo resuelve el frontend via
   * renegociacion (offer/answer con la nueva track de video); el server solo
   * cambia `kind` para consumers/analytics y para que futuros joiners sepan
   * que la conversacion es Video.
   */
  upgradeToVideo(input: { actorUserId: string; now?: Date }): Result<void> {
    if (this.state.status !== CallStatus.Active) {
      return Result.fail(
        makeError('Call.Upgrade.InvalidState', `Cannot upgrade a call in status ${this.state.status}.`),
      );
    }
    if (this.state.kind === CallKind.Video) {
      return Result.fail(makeError('Call.Upgrade.AlreadyVideo', 'Call is already a video call.'));
    }
    if (!this.isParticipant(input.actorUserId)) {
      return Result.fail(makeError('Call.NotParticipant', 'User is not a participant of this call.'));
    }
    const now = input.now ?? new Date();
    this.state = { ...this.state, kind: CallKind.Video, updatedAtUtc: now };
    return Result.okVoid();
  }

  /**
   * Fase Backend 7 — screen share con senal dedicada (no piggy-back en
   * media_status). Invariante 1:1: solo un participante puede compartir a la
   * vez. Devuelve el `startedAtUtc` para que el use case lo publique en el
   * evento sin re-leer el snapshot.
   */
  startScreenShare(input: { actorUserId: string; now?: Date }): Result<{ startedAtUtc: Date }> {
    if (this.state.status !== CallStatus.Active) {
      return Result.fail(
        makeError('Call.ScreenShare.InvalidState', `Cannot start screen share in status ${this.state.status}.`),
      );
    }
    const participant = this.participants.find((p) => p.userId === input.actorUserId);
    if (!participant) {
      return Result.fail(makeError('Call.NotParticipant', 'User is not a participant of this call.'));
    }
    const otherSharing = this.participants.find((p) => p.userId !== input.actorUserId && p.screenSharing);
    if (otherSharing) {
      return Result.fail(
        makeError(
          'Call.ScreenShare.AnotherActive',
          'Another participant is already screen sharing. Stop that first.',
        ),
      );
    }
    const now = input.now ?? new Date();
    const startResult = participant.startScreenSharing(now);
    if (!startResult.isSuccess) return Result.fail(startResult.error);
    this.state = {
      ...this.state,
      updatedAtUtc: now,
      participants: this.participants.map((p) => p.toSnapshot()),
    };
    return Result.ok({ startedAtUtc: now });
  }

  /**
   * Solo el que empezo el screen share puede pararlo (mismo criterio que
   * `attachCallRecording` en Fase 4). Si la llamada termina antes, el
   * `end()`/`fail()` ya cierra a todos los participantes — no hace falta
   * un stop implicito aca.
   */
  stopScreenShare(input: {
    actorUserId: string;
    now?: Date;
  }): Result<{ startedAtUtc: Date; stoppedAtUtc: Date; durationSeconds: number }> {
    const participant = this.participants.find((p) => p.userId === input.actorUserId);
    if (!participant) {
      return Result.fail(makeError('Call.NotParticipant', 'User is not a participant of this call.'));
    }
    const stopResult = participant.stopScreenSharing();
    if (!stopResult.isSuccess) return Result.fail(stopResult.error);
    const now = input.now ?? new Date();
    const durationSeconds = Math.max(0, Math.floor((now.getTime() - stopResult.value.startedAtUtc.getTime()) / 1000));
    this.state = {
      ...this.state,
      updatedAtUtc: now,
      participants: this.participants.map((p) => p.toSnapshot()),
    };
    return Result.ok({ startedAtUtc: stopResult.value.startedAtUtc, stoppedAtUtc: now, durationSeconds });
  }

  applyMediaStatus(input: { byUserId: string; status: MediaStatus }): Result<void> {
    if (this.state.status !== CallStatus.Active && this.state.status !== CallStatus.Accepted) {
      return Result.fail(
        makeError('Call.MediaStatus.InvalidState', `Cannot update media status while ${this.state.status}.`),
      );
    }
    const participant = this.participants.find((p) => p.userId === input.byUserId);
    if (!participant) {
      return Result.fail(makeError('Call.NotParticipant', 'User is not a participant of this call.'));
    }
    const result = participant.applyMediaStatus(input.byUserId, input.status);
    if (!result.isSuccess) return result;
    this.state = {
      ...this.state,
      updatedAtUtc: new Date(),
      participants: this.participants.map((p) => p.toSnapshot()),
    };
    return Result.okVoid();
  }

  reportConnectionQuality(input: { byUserId: string; quality: ConnectionQuality }): Result<void> {
    const participant = this.participants.find((p) => p.userId === input.byUserId);
    if (!participant) {
      return Result.fail(makeError('Call.NotParticipant', 'User is not a participant of this call.'));
    }
    participant.updateConnectionQuality(input.quality);
    this.state = {
      ...this.state,
      updatedAtUtc: new Date(),
      participants: this.participants.map((p) => p.toSnapshot()),
    };
    return Result.okVoid();
  }

  attachRecording(fileId: string): Result<void> {
    if (this.state.status !== CallStatus.Ended && this.state.status !== CallStatus.Active) {
      return Result.fail(
        makeError('Call.Recording.InvalidState', `Cannot attach recording in status ${this.state.status}.`),
      );
    }
    if (this.state.recordingFileId !== null) {
      return Result.fail(makeError('Call.Recording.Duplicate', 'Recording already attached.'));
    }
    this.state = {
      ...this.state,
      recordingFileId: fileId,
      updatedAtUtc: new Date(),
    };
    return Result.okVoid();
  }

  /**
   * Adjunta el transcript STT generado por el worker de transcripts a partir
   * de `recordingFileId`. Mismo criterio que attachRecording (Ended o Active)
   * — la premisa original de "el transcript siempre llega con la call ya
   * Ended" no se sostiene: el pipeline de transcripcion (download+ffmpeg+
   * whisper.cpp) es asincronico y puede tardar mas de lo que tarda la call
   * en terminar realmente, y TranscriptReady llegaba con la call todavia
   * Active — attachCallTranscript quedaba huerfano (Call.Transcript.InvalidState,
   * warn-and-drop en el consumer, sin retry) aunque el archivo ya existiera
   * en CloudStorage.
   */
  attachTranscript(fileId: string): Result<void> {
    if (this.state.status !== CallStatus.Ended && this.state.status !== CallStatus.Active) {
      return Result.fail(
        makeError('Call.Transcript.InvalidState', `Cannot attach transcript in status ${this.state.status}.`),
      );
    }
    if (this.state.transcriptFileId !== null) {
      return Result.fail(makeError('Call.Transcript.Duplicate', 'Transcript already attached.'));
    }
    this.state = {
      ...this.state,
      transcriptFileId: fileId,
      updatedAtUtc: new Date(),
    };
    return Result.okVoid();
  }

  // ------------------------------------------------------------------
  // Recording consent + state machine (Fase Backend 2) — ver docblock
  // equivalente en meeting.ts. Call no tiene concepto de host: la autorizacion
  // es simplemente "es caller o callee" (isParticipant), y la policy por
  // default en Fase Backend 4 sera AllAcceptedRequired (solo 2 partes, sin
  // ambiguedad de "no respondio").
  // ------------------------------------------------------------------

  requestRecording(input: {
    actorUserId: string;
    existingSession: RecordingSession | null;
    now?: Date;
  }): Result<{ session: RecordingSession; participantUserIds: readonly string[] }> {
    if (!this.isParticipant(input.actorUserId)) {
      return Result.fail(makeError('Call.NotParticipant', 'User is not a participant of this call.'));
    }
    if (input.existingSession !== null) {
      return Result.fail(
        makeError('Call.Recording.AlreadyRequested', 'A recording session already exists for this call.'),
      );
    }
    const sessionResult = RecordingSession.request({
      tenantId: this.state.tenantId,
      scope: RecordingScope.Call,
      scopeId: this.state.id,
      requestedByUserId: input.actorUserId,
      now: input.now ?? new Date(),
    });
    if (!sessionResult.isSuccess) return sessionResult;
    return Result.ok({
      session: sessionResult.value,
      participantUserIds: [this.state.callerUserId, this.state.calleeUserId],
    });
  }

  recordConsent(input: {
    userId: string;
    response: RecordingConsentEntryStatus;
    respondedAtUtc?: Date;
    now?: Date;
  }): Result<RecordingConsentEntry> {
    if (!this.isParticipant(input.userId)) {
      return Result.fail(makeError('Call.NotParticipant', 'User is not a participant of this call.'));
    }
    return RecordingConsentEntry.record({
      tenantId: this.state.tenantId,
      scope: RecordingScope.Call,
      scopeId: this.state.id,
      userId: input.userId,
      response: input.response,
      respondedAtUtc: input.respondedAtUtc ?? input.now ?? new Date(),
      now: input.now ?? new Date(),
    });
  }

  private ensureSessionBelongsHere(session: RecordingSession): Result<void> {
    if (session.scope !== RecordingScope.Call || session.scopeId !== this.state.id) {
      return Result.fail(makeError('Call.Recording.SessionMismatch', 'Session does not belong to this call.'));
    }
    return Result.okVoid();
  }

  startRecording(input: {
    actorUserId: string;
    session: RecordingSession;
    policyAllows: boolean;
    now?: Date;
  }): Result<RecordingSession> {
    if (!this.isParticipant(input.actorUserId)) {
      return Result.fail(makeError('Call.NotParticipant', 'User is not a participant of this call.'));
    }
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.start({ policyAllows: input.policyAllows, now: input.now ?? new Date() });
    if (!result.isSuccess) return result;
    return Result.ok(input.session);
  }

  stopRecording(input: { actorUserId: string; session: RecordingSession; now?: Date }): Result<RecordingSession> {
    if (!this.isParticipant(input.actorUserId)) {
      return Result.fail(makeError('Call.NotParticipant', 'User is not a participant of this call.'));
    }
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.stop(input.now);
    if (!result.isSuccess) return result;
    return Result.ok(input.session);
  }

  /**
   * Stopping -> Processing, disparado por attach-call-recording.ts al recibir
   * el fileId. Sin chequeo de actor propio — el caller ya valida "es quien
   * pidio la grabacion" antes de llegar aca (mas estricto que meetings, que
   * acepta cualquier Host/Cohost).
   */
  beginProcessingRecording(input: { session: RecordingSession }): Result<RecordingSession> {
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.beginProcessing();
    if (!result.isSuccess) return result;
    return Result.ok(input.session);
  }

  /** Sin chequeo de actor — se dispara tanto por un participante como por el sistema (transcript worker, Fase Backend 4). */
  failRecording(input: { session: RecordingSession; reason: string; now?: Date }): Result<RecordingSession> {
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.fail({ reason: input.reason, now: input.now ?? new Date() });
    if (!result.isSuccess) return result;
    return Result.ok(input.session);
  }

  /** System-triggered (transcript worker confirma Ready) — sin chequeo de actor. */
  completeRecording(input: {
    session: RecordingSession;
    recordingFileId: string;
    durationSeconds: number;
    now?: Date;
  }): Result<RecordingSession> {
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.complete({
      recordingFileId: input.recordingFileId,
      durationSeconds: input.durationSeconds,
      now: input.now ?? new Date(),
    });
    if (!result.isSuccess) return result;
    return Result.ok(input.session);
  }

  toSnapshot(): CallSnapshot {
    return this.state;
  }

  get id(): string {
    return this.state.id;
  }
  get tenantId(): string {
    return this.state.tenantId;
  }
  get status(): CallStatus {
    return this.state.status;
  }
  get callerUserId(): string {
    return this.state.callerUserId;
  }
  get calleeUserId(): string {
    return this.state.calleeUserId;
  }

  isParticipant(userId: string): boolean {
    return this.state.callerUserId === userId || this.state.calleeUserId === userId;
  }

  getPeerUserId(userId: string): string | null {
    if (userId === this.state.callerUserId) return this.state.calleeUserId;
    if (userId === this.state.calleeUserId) return this.state.callerUserId;
    return null;
  }
}
