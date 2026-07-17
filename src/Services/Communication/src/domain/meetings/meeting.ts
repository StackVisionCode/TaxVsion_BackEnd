import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import {
  MeetingRole,
  MeetingStatus,
  MeetingStrategy,
  ParticipantStatus,
  isMeetingStrategy,
} from './meeting-enums.js';
import { MeetingParticipant, type MeetingParticipantSnapshot } from './meeting-participant.js';
import { generateShortCode } from './short-code.js';
import type { ConnectionQuality, MediaStatus } from '../calls/media-status.js';
import { RecordingSession } from '../recording/recording-session.js';
import { RecordingScope } from '../recording/recording-session-state.js';
import { RecordingConsentEntry } from '../recording/recording-consent-entry.js';
import type { RecordingConsentEntryStatus } from '../recording/recording-consent.js';

export interface MeetingSnapshot {
  readonly id: string;
  readonly tenantId: string;
  readonly title: string;
  readonly description: string | null;
  readonly status: MeetingStatus;
  readonly shortCode: string;
  readonly passcodeHash: string | null;
  readonly requireWaitingRoom: boolean;
  readonly isLocked: boolean;
  readonly maxParticipants: number;
  readonly strategy: MeetingStrategy;
  readonly recordingRequested: boolean;
  readonly recordingFileId: string | null;
  readonly transcriptFileId: string | null;
  readonly hostUserId: string;
  readonly scheduledForUtc: Date | null;
  readonly startedAtUtc: Date | null;
  readonly endedAtUtc: Date | null;
  readonly durationSeconds: number | null;
  readonly createdByUserId: string;
  readonly createdAtUtc: Date;
  readonly updatedAtUtc: Date;
  readonly participants: readonly MeetingParticipantSnapshot[];
}

export interface ScheduleMeetingInput {
  readonly tenantId: string;
  readonly title: string;
  readonly description?: string | null;
  readonly host: { userId: string; displayName: string };
  readonly maxParticipants?: number;
  readonly requireWaitingRoom?: boolean;
  readonly passcodeHash?: string | null;
  readonly scheduledForUtc?: Date | null;
  readonly recordingRequested?: boolean;
  readonly now?: Date;
}

export class Meeting {
  private state: MeetingSnapshot;
  private participants: MeetingParticipant[];

  private constructor(snapshot: MeetingSnapshot) {
    this.state = snapshot;
    this.participants = snapshot.participants.map(MeetingParticipant.rehydrate);
  }

  static rehydrate(snapshot: MeetingSnapshot): Meeting {
    return new Meeting(snapshot);
  }

  static schedule(input: ScheduleMeetingInput): Result<Meeting> {
    if (input.title.trim().length === 0) {
      return Result.fail(makeError('Meeting.MissingTitle', 'Title is required.'));
    }
    const now = input.now ?? new Date();
    const maxParticipants = input.maxParticipants ?? 4;
    if (maxParticipants < 2 || maxParticipants > 100) {
      return Result.fail(makeError('Meeting.InvalidMax', 'maxParticipants must be between 2 and 100.'));
    }
    const id = randomUUID();
    const hostParticipant = MeetingParticipant.create({
      meetingId: id,
      tenantId: input.tenantId,
      userId: input.host.userId,
      displayName: input.host.displayName,
      role: MeetingRole.Host,
      status: ParticipantStatus.Left, // aun no ha entrado; entra al Start.
      joinOrder: 1,
      audioDefault: true,
      videoDefault: true,
      now,
    });
    const snapshot: MeetingSnapshot = {
      id,
      tenantId: input.tenantId,
      title: input.title.trim().slice(0, 200),
      description: input.description ? input.description.trim().slice(0, 1000) : null,
      status: MeetingStatus.Scheduled,
      shortCode: generateShortCode(),
      passcodeHash: input.passcodeHash ?? null,
      requireWaitingRoom: input.requireWaitingRoom ?? true,
      isLocked: false,
      maxParticipants,
      strategy: maxParticipants <= 4 ? MeetingStrategy.Mesh : MeetingStrategy.Sfu,
      recordingRequested: input.recordingRequested ?? false,
      recordingFileId: null,
      transcriptFileId: null,
      hostUserId: input.host.userId,
      scheduledForUtc: input.scheduledForUtc ?? null,
      startedAtUtc: null,
      endedAtUtc: null,
      durationSeconds: null,
      createdByUserId: input.host.userId,
      createdAtUtc: now,
      updatedAtUtc: now,
      participants: [hostParticipant.toSnapshot()],
    };
    return Result.ok(new Meeting(snapshot));
  }

  requestJoin(input: {
    userId: string;
    displayName: string;
    actorType?: string;
    hasValidInvitation: boolean;
    passcodeMatch: boolean | null;
    audioDefault?: boolean;
    videoDefault?: boolean;
    now?: Date;
  }): Result<{ requiresAdmission: boolean; role: MeetingRole }> {
    if (this.state.status === MeetingStatus.Ended || this.state.status === MeetingStatus.Cancelled) {
      return Result.fail(makeError('Meeting.NotJoinable', `Meeting is ${this.state.status}.`));
    }
    if (this.state.isLocked && !input.hasValidInvitation && input.userId !== this.state.hostUserId) {
      return Result.fail(makeError('Meeting.Locked', 'Meeting is locked. Invitation required.'));
    }
    if (this.state.passcodeHash && input.passcodeMatch === false) {
      return Result.fail(makeError('Meeting.InvalidPasscode', 'Wrong passcode.'));
    }
    const existing = this.participants.find((p) => p.userId === input.userId);
    if (existing && existing.status === 'Joined') {
      return Result.ok({ requiresAdmission: false, role: existing.role });
    }
    if (existing && existing.status === 'Waiting') {
      return Result.ok({ requiresAdmission: true, role: existing.role });
    }

    const now = input.now ?? new Date();
    const isHost = input.userId === this.state.hostUserId;
    const activeJoined = this.participants.filter((p) => p.isJoined).length;
    if (!isHost && activeJoined >= this.state.maxParticipants) {
      return Result.fail(makeError('Meeting.Full', 'Meeting is full.'));
    }

    const requiresAdmission = !isHost && this.state.requireWaitingRoom;
    const role: MeetingRole = isHost ? MeetingRole.Host : MeetingRole.Attendee;
    const joinOrder = this.nextJoinOrder();

    const participant = MeetingParticipant.create({
      meetingId: this.state.id,
      tenantId: this.state.tenantId,
      userId: input.userId,
      displayName: input.displayName,
      ...(input.actorType !== undefined ? { actorType: input.actorType } : {}),
      role,
      status: requiresAdmission ? ParticipantStatus.Waiting : ParticipantStatus.Joined,
      joinOrder,
      audioDefault: input.audioDefault ?? true,
      videoDefault: input.videoDefault ?? true,
      now,
    });

    if (existing) {
      this.participants = this.participants.filter((p) => p.userId !== input.userId);
    }
    this.participants.push(participant);
    this.commit(now);
    return Result.ok({ requiresAdmission, role });
  }

  admit(input: { hostUserId: string; targetUserId: string; now?: Date }): Result<void> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return guard;
    const target = this.participants.find((p) => p.userId === input.targetUserId);
    if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target participant not found.'));
    const now = input.now ?? new Date();
    const result = target.admit(now);
    if (!result.isSuccess) return result;
    this.commit(now);
    return Result.okVoid();
  }

  leave(input: { userId: string; now?: Date }): Result<void> {
    const participant = this.participants.find((p) => p.userId === input.userId);
    if (!participant) return Result.fail(makeError('Meeting.NotParticipant', 'Not a participant.'));
    const now = input.now ?? new Date();
    participant.markLeft(now);
    // Host se va -> ceder host a cohost mas antiguo, o terminar el meeting.
    if (input.userId === this.state.hostUserId) {
      const newHost = this.selectNextHost();
      if (newHost) {
        newHost.transferHost();
        this.state = { ...this.state, hostUserId: newHost.userId };
      } else {
        this.commit(now);
        return this.end({ byUserId: input.userId, now });
      }
    }
    this.commit(now);
    return Result.okVoid();
  }

  start(input: { hostUserId: string; audioDefault?: boolean; videoDefault?: boolean; now?: Date }): Result<void> {
    if (this.state.status !== MeetingStatus.Scheduled) {
      return Result.fail(makeError('Meeting.InvalidTransition', `Cannot start from ${this.state.status}.`));
    }
    if (input.hostUserId !== this.state.hostUserId) {
      return Result.fail(makeError('Meeting.HostOnly', 'Only the host can start the meeting.'));
    }
    const now = input.now ?? new Date();
    // El host se re-crea como Joined — el placeholder Scheduled deja al host en
    // Left para no contarlo en `activeJoined`. Al iniciar, el host cuenta como
    // primer joined y ocupa un slot del maxParticipants.
    this.participants = this.participants.filter((p) => p.userId !== this.state.hostUserId);
    const hostParticipant = MeetingParticipant.create({
      meetingId: this.state.id,
      tenantId: this.state.tenantId,
      userId: this.state.hostUserId,
      displayName:
        this.state.participants.find((p) => p.userId === this.state.hostUserId)?.displayName ?? 'Host',
      role: MeetingRole.Host,
      status: ParticipantStatus.Joined,
      joinOrder: 1,
      audioDefault: input.audioDefault ?? true,
      videoDefault: input.videoDefault ?? true,
      now,
    });
    this.participants.push(hostParticipant);
    this.state = {
      ...this.state,
      status: MeetingStatus.Live,
      startedAtUtc: now,
      updatedAtUtc: now,
      participants: this.participants.map((p) => p.toSnapshot()),
    };
    return Result.okVoid();
  }

  end(input: { byUserId: string; now?: Date }): Result<void> {
    if (this.state.status === MeetingStatus.Ended || this.state.status === MeetingStatus.Cancelled) {
      return Result.fail(makeError('Meeting.AlreadyEnded', `Meeting already ${this.state.status}.`));
    }
    if (input.byUserId !== this.state.hostUserId && !this.isCohost(input.byUserId)) {
      return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can end the meeting.'));
    }
    const now = input.now ?? new Date();
    this.participants.forEach((p) => {
      if (p.status === 'Joined' || p.status === 'Waiting') p.markLeft(now);
    });
    const startedAt = this.state.startedAtUtc;
    this.state = {
      ...this.state,
      status: MeetingStatus.Ended,
      endedAtUtc: now,
      durationSeconds: startedAt ? Math.max(0, Math.floor((now.getTime() - startedAt.getTime()) / 1000)) : 0,
      updatedAtUtc: now,
      participants: this.participants.map((p) => p.toSnapshot()),
    };
    return Result.okVoid();
  }

  cancel(input: { hostUserId: string; now?: Date }): Result<void> {
    if (this.state.status !== MeetingStatus.Scheduled) {
      return Result.fail(makeError('Meeting.InvalidTransition', `Cannot cancel from ${this.state.status}.`));
    }
    if (input.hostUserId !== this.state.hostUserId) {
      return Result.fail(makeError('Meeting.HostOnly', 'Only the host can cancel.'));
    }
    const now = input.now ?? new Date();
    this.state = { ...this.state, status: MeetingStatus.Cancelled, endedAtUtc: now, updatedAtUtc: now };
    return Result.okVoid();
  }

  /**
   * Solo valido en estado Scheduled — un meeting Live/Ended no se re-agenda.
   * `newScheduledForUtc` puede ser null (des-agendar). El aggregate no valida
   * "esta en el futuro" — esa politica de negocio la aplica el use case porque
   * puede depender de tenant setting (ej. permitir re-agendar en el pasado
   * para meetings historicos de auditoria).
   */
  reschedule(input: { hostUserId: string; newScheduledForUtc: Date | null; now?: Date }): Result<void> {
    if (this.state.status !== MeetingStatus.Scheduled) {
      return Result.fail(makeError('Meeting.InvalidTransition', `Cannot reschedule from ${this.state.status}.`));
    }
    if (input.hostUserId !== this.state.hostUserId && !this.isCohost(input.hostUserId)) {
      return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can reschedule.'));
    }
    const now = input.now ?? new Date();
    this.state = {
      ...this.state,
      scheduledForUtc: input.newScheduledForUtc,
      updatedAtUtc: now,
    };
    return Result.okVoid();
  }

  /**
   * Host/Cohost rechaza un participante de la sala de espera — solo valido si
   * el target esta en `Waiting`. Distinto de `removeParticipant` (que aplica a
   * cualquier estado y no cambia hostUserId): denyWaitingRoom es especifico de
   * la sala de espera, por eso emite su propio evento (Notification puede
   * enviar un mail "tu solicitud fue rechazada").
   */
  denyWaitingRoom(input: { hostUserId: string; targetUserId: string; now?: Date }): Result<void> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return guard;
    const target = this.participants.find((p) => p.userId === input.targetUserId);
    if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target participant not found.'));
    if (target.status !== 'Waiting') {
      return Result.fail(makeError('Meeting.NotWaiting', `Cannot deny a ${target.status} participant.`));
    }
    const now = input.now ?? new Date();
    target.remove(now);
    this.commit(now);
    return Result.okVoid();
  }

  setLocked(input: { hostUserId: string; locked: boolean }): Result<void> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return guard;
    this.state = { ...this.state, isLocked: input.locked, updatedAtUtc: new Date() };
    return Result.okVoid();
  }

  muteAll(input: { hostUserId: string }): Result<void> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return guard;
    this.participants.forEach((p) => {
      if (p.userId !== input.hostUserId) p.forceMuteAudio();
    });
    this.commit(new Date());
    return Result.okVoid();
  }

  removeParticipant(input: { hostUserId: string; targetUserId: string; now?: Date }): Result<void> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return guard;
    if (input.targetUserId === this.state.hostUserId) {
      return Result.fail(makeError('Meeting.CannotRemoveHost', 'Host cannot be removed. Transfer host first.'));
    }
    const target = this.participants.find((p) => p.userId === input.targetUserId);
    if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target participant not found.'));
    const now = input.now ?? new Date();
    target.remove(now);
    this.commit(now);
    return Result.okVoid();
  }

  transferHost(input: { currentHostUserId: string; newHostUserId: string; now?: Date }): Result<void> {
    if (input.currentHostUserId !== this.state.hostUserId) {
      return Result.fail(makeError('Meeting.HostOnly', 'Only current host can transfer.'));
    }
    if (input.currentHostUserId === input.newHostUserId) {
      return Result.fail(makeError('Meeting.NoOp', 'Cannot transfer host to self.'));
    }
    const current = this.participants.find((p) => p.userId === input.currentHostUserId);
    const next = this.participants.find((p) => p.userId === input.newHostUserId && p.isJoined);
    if (!next) return Result.fail(makeError('Meeting.TargetNotJoined', 'Target must be a joined participant.'));
    if (current) current.demoteHostToCohost();
    next.transferHost();
    const now = input.now ?? new Date();
    this.state = { ...this.state, hostUserId: input.newHostUserId, updatedAtUtc: now };
    this.commit(now);
    return Result.okVoid();
  }

  promoteToCohost(input: { hostUserId: string; targetUserId: string }): Result<void> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return guard;
    const target = this.participants.find((p) => p.userId === input.targetUserId);
    if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target participant not found.'));
    const result = target.promoteToCohost();
    if (!result.isSuccess) return result;
    this.commit(new Date());
    return Result.okVoid();
  }

  demoteToAttendee(input: { hostUserId: string; targetUserId: string }): Result<void> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return guard;
    const target = this.participants.find((p) => p.userId === input.targetUserId);
    if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target participant not found.'));
    const result = target.demoteToAttendee();
    if (!result.isSuccess) return result;
    this.commit(new Date());
    return Result.okVoid();
  }

  applyMediaStatus(input: { byUserId: string; status: MediaStatus }): Result<void> {
    const participant = this.participants.find((p) => p.userId === input.byUserId && p.isJoined);
    if (!participant) return Result.fail(makeError('Meeting.NotParticipant', 'Not a joined participant.'));
    participant.applyMediaStatus(input.status);
    this.commit(new Date());
    return Result.okVoid();
  }

  raiseHand(input: { byUserId: string; raised: boolean }): Result<void> {
    const participant = this.participants.find((p) => p.userId === input.byUserId && p.isJoined);
    if (!participant) return Result.fail(makeError('Meeting.NotParticipant', 'Not a joined participant.'));
    participant.setHandRaised(input.raised);
    this.commit(new Date());
    return Result.okVoid();
  }

  reportConnectionQuality(input: { byUserId: string; quality: ConnectionQuality }): Result<void> {
    const participant = this.participants.find((p) => p.userId === input.byUserId);
    if (!participant) return Result.fail(makeError('Meeting.NotParticipant', 'Not a participant.'));
    participant.updateConnectionQuality(input.quality);
    this.commit(new Date());
    return Result.okVoid();
  }

  attachRecording(fileId: string): Result<void> {
    if (this.state.status !== MeetingStatus.Ended && this.state.status !== MeetingStatus.Live) {
      return Result.fail(
        makeError('Meeting.Recording.InvalidState', `Cannot attach recording in status ${this.state.status}.`),
      );
    }
    if (this.state.recordingFileId !== null) {
      return Result.fail(makeError('Meeting.Recording.Duplicate', 'Recording already attached.'));
    }
    this.state = { ...this.state, recordingFileId: fileId, updatedAtUtc: new Date() };
    return Result.okVoid();
  }

  /**
   * Mismo criterio que attachRecording (Ended o Live) — la premisa original
   * de "el transcript siempre llega con el meeting ya Ended" no se sostiene:
   * el pipeline de transcripcion (download+ffmpeg+whisper.cpp) es
   * asincronico y puede tardar mas que lo que el host tarda en cerrar el
   * meeting, o el host simplemente sigue reunido despues de cortar la
   * grabacion — TranscriptReady llegaba con el meeting todavia Live y
   * attachMeetingTranscript quedaba huerfano (Meeting.Transcript.InvalidState,
   * warn-and-drop en el consumer, sin retry) aunque el archivo ya existiera
   * en CloudStorage.
   */
  attachTranscript(fileId: string): Result<void> {
    if (this.state.status !== MeetingStatus.Ended && this.state.status !== MeetingStatus.Live) {
      return Result.fail(
        makeError('Meeting.Transcript.InvalidState', `Cannot attach transcript in status ${this.state.status}.`),
      );
    }
    if (this.state.transcriptFileId !== null) {
      return Result.fail(makeError('Meeting.Transcript.Duplicate', 'Transcript already attached.'));
    }
    this.state = { ...this.state, transcriptFileId: fileId, updatedAtUtc: new Date() };
    return Result.okVoid();
  }

  // ------------------------------------------------------------------
  // Recording consent + state machine (Fase Backend 2). RecordingSession y
  // RecordingConsentEntry persisten en tablas propias (Scope='Meeting',
  // ScopeId=this.id) — NO son parte de MeetingSnapshot ni de la fila Meeting.
  // El caller (use case) es responsable de cargar/guardar la sesion via
  // RecordingSessionRepository/RecordingConsentRepository; estos metodos solo
  // protegen las invariantes (quien puede actuar, que transiciones son
  // validas). Ningun evento de integracion se publica desde aqui todavia —
  // eso es Fase Backend 3, que leera el `session`/`entry` devuelto para
  // construir el payload correspondiente.
  // ------------------------------------------------------------------

  requestRecording(input: {
    actorUserId: string;
    existingSession: RecordingSession | null;
    now?: Date;
  }): Result<{ session: RecordingSession; participantUserIds: readonly string[] }> {
    const guard = this.ensureCanHostAct(input.actorUserId);
    if (!guard.isSuccess) return guard;
    if (input.existingSession !== null) {
      return Result.fail(
        makeError('Meeting.Recording.AlreadyRequested', 'A recording session already exists for this meeting.'),
      );
    }
    const sessionResult = RecordingSession.request({
      tenantId: this.state.tenantId,
      scope: RecordingScope.Meeting,
      scopeId: this.state.id,
      requestedByUserId: input.actorUserId,
      now: input.now ?? new Date(),
    });
    if (!sessionResult.isSuccess) return sessionResult;
    const participantUserIds = this.getJoinedParticipants().map((p) => p.userId);
    return Result.ok({ session: sessionResult.value, participantUserIds });
  }

  /** Valida que quien responde sea (o haya sido) participante y delega el append-only log a RecordingConsentEntry. */
  recordConsent(input: {
    userId: string;
    response: RecordingConsentEntryStatus;
    respondedAtUtc?: Date;
    now?: Date;
  }): Result<RecordingConsentEntry> {
    const isKnownParticipant = this.participants.some((p) => p.userId === input.userId);
    if (!isKnownParticipant) {
      return Result.fail(makeError('Meeting.NotParticipant', 'Not a participant of this meeting.'));
    }
    return RecordingConsentEntry.record({
      tenantId: this.state.tenantId,
      scope: RecordingScope.Meeting,
      scopeId: this.state.id,
      userId: input.userId,
      response: input.response,
      respondedAtUtc: input.respondedAtUtc ?? input.now ?? new Date(),
      now: input.now ?? new Date(),
    });
  }

  private ensureSessionBelongsHere(session: RecordingSession): Result<void> {
    if (session.scope !== RecordingScope.Meeting || session.scopeId !== this.state.id) {
      return Result.fail(
        makeError('Meeting.Recording.SessionMismatch', 'Session does not belong to this meeting.'),
      );
    }
    return Result.okVoid();
  }

  startRecording(input: {
    actorUserId: string;
    session: RecordingSession;
    policyAllows: boolean;
    now?: Date;
  }): Result<RecordingSession> {
    const guard = this.ensureCanHostAct(input.actorUserId);
    if (!guard.isSuccess) return guard;
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.start({ policyAllows: input.policyAllows, now: input.now ?? new Date() });
    if (!result.isSuccess) return result;
    return Result.ok(input.session);
  }

  stopRecording(input: { actorUserId: string; session: RecordingSession; now?: Date }): Result<RecordingSession> {
    const guard = this.ensureCanHostAct(input.actorUserId);
    if (!guard.isSuccess) return guard;
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.stop(input.now);
    if (!result.isSuccess) return result;
    return Result.ok(input.session);
  }

  /**
   * Stopping -> Processing, disparado por attach-meeting-recording.ts al
   * recibir el fileId. Sin chequeo de actor propio — el caller (Fase Backend 3)
   * ya valida Host/Cohost antes de llegar aca, igual que failRecording/completeRecording.
   */
  beginProcessingRecording(input: { session: RecordingSession }): Result<RecordingSession> {
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.beginProcessing();
    if (!result.isSuccess) return result;
    return Result.ok(input.session);
  }

  /** Sin chequeo de actor — se dispara tanto por el host como por el sistema (transcript worker, Fase Backend 3). */
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

  toSnapshot(): MeetingSnapshot {
    return this.state;
  }

  get id(): string {
    return this.state.id;
  }
  get tenantId(): string {
    return this.state.tenantId;
  }
  get status(): MeetingStatus {
    return this.state.status;
  }
  get hostUserId(): string {
    return this.state.hostUserId;
  }
  get strategy(): MeetingStrategy {
    return this.state.strategy;
  }
  get requiresPasscode(): boolean {
    return this.state.passcodeHash !== null;
  }
  get passcodeHash(): string | null {
    return this.state.passcodeHash;
  }

  getJoinedParticipants(): readonly MeetingParticipantSnapshot[] {
    return this.participants.filter((p) => p.isJoined).map((p) => p.toSnapshot());
  }

  isJoinedParticipant(userId: string): boolean {
    return this.participants.some((p) => p.userId === userId && p.isJoined);
  }

  isCohost(userId: string): boolean {
    return this.participants.some((p) => p.userId === userId && p.role === 'Cohost' && p.isJoined);
  }

  // ------------------------------------------------------------------
  // Helpers privados
  // ------------------------------------------------------------------

  private ensureCanHostAct(userId: string): Result<void> {
    if (userId !== this.state.hostUserId && !this.isCohost(userId)) {
      return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can perform this action.'));
    }
    return Result.okVoid();
  }

  private selectNextHost(): MeetingParticipant | null {
    const cohosts = this.participants.filter((p) => p.isJoined && p.role === 'Cohost');
    if (cohosts.length > 0) {
      return cohosts.sort((a, b) => a.joinOrder - b.joinOrder)[0]!;
    }
    const attendees = this.participants.filter((p) => p.isJoined && p.role === 'Attendee');
    if (attendees.length > 0) return attendees.sort((a, b) => a.joinOrder - b.joinOrder)[0]!;
    return null;
  }

  private nextJoinOrder(): number {
    return this.participants.reduce((max, p) => Math.max(max, p.joinOrder), 0) + 1;
  }

  private commit(now: Date): void {
    this.state = {
      ...this.state,
      updatedAtUtc: now,
      strategy: isMeetingStrategy(this.state.strategy) ? this.state.strategy : MeetingStrategy.Mesh,
      participants: this.participants.map((p) => p.toSnapshot()),
    };
  }
}
