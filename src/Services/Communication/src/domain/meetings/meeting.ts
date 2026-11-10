import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import {
  MeetingEndReason,
  MeetingRole,
  MeetingStatus,
  MeetingStrategy,
  ParticipantStatus,
  isMeetingStrategy,
  isTerminalMeetingStatus,
} from './meeting-enums.js';
import { MeetingParticipant, type MeetingParticipantSnapshot } from './meeting-participant.js';
import { generateShortCode } from './short-code.js';
import type { ConnectionQuality, MediaStatus } from '../calls/media-status.js';
import { RecordingSession } from '../recording/recording-session.js';
import { RecordingScope, type RecordingCaptureMode, type RecordingStopReason } from '../recording/recording-session-state.js';
import { RecordingConsentEntry } from '../recording/recording-consent-entry.js';
import type { RecordingConsentEntryStatus } from '../recording/recording-consent.js';
import { TECHNICAL_LIMITS, isTranscriptLanguage } from '../settings/technical-limits.js';

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
  /** D-69: esta reunion admite invitados externos (nunca por encima de lo que permite la oficina). */
  readonly guestsAllowed: boolean;
  /** D-68: enlace de la reunion para externos; la version invalida los enlaces anteriores. */
  readonly guestLinkEnabled: boolean;
  readonly guestLinkVersion: number;
  readonly maxParticipants: number;
  readonly strategy: MeetingStrategy;
  readonly recordingRequested: boolean;
  readonly recordingFileId: string | null;
  readonly transcriptFileId: string | null;
  /**
   * Idioma del transcript de esta reunión (INV-T01 R-7): `en`, `es`… o `auto`. null = automático: cada
   * participante con el idioma que eligió para su voz y, si no eligió, el de la oficina.
   */
  readonly transcriptLanguage: string | null;
  readonly hostUserId: string;
  readonly scheduledForUtc: Date | null;
  /** Informativa (invitación/calendario); nunca corta la reunión (D-35). */
  readonly scheduledDurationMinutes: number | null;
  /** Fijado al Start con el valor efectivo de la oficina; es el que corta (D-34). */
  readonly maxDurationMinutes: number | null;
  readonly endsAtUtc: Date | null;
  readonly endReason: MeetingEndReason | null;
  readonly lastDurationWarningSeconds: number | null;
  /** F6 (T-64): recordatorio T-N enviado (una sola vez). */
  readonly reminderSentAtUtc: Date | null;
  readonly continuationOfMeetingId: string | null;
  /** Cita de Calendar de la que nació la sala (una sala por cita). */
  readonly appointmentId?: string | null;
  readonly startedAtUtc: Date | null;
  readonly endedAtUtc: Date | null;
  readonly durationSeconds: number | null;
  readonly createdByUserId: string;
  readonly createdAtUtc: Date;
  readonly updatedAtUtc: Date;
  readonly participants: readonly MeetingParticipantSnapshot[];
}

/**
 * Politica de media con la que se decide la estrategia (mesh vs SFU) y el cupo por defecto.
 * La trae la aplicacion (resolver de settings); el dominio solo la aplica.
 * `DEFAULT_MEETING_MEDIA_POLICY` es el respaldo tecnico cuando nadie la pasa.
 */
export interface MeetingMediaPolicy {
  /** Hasta cuantos participantes (cupo) se usa mesh; por encima, SFU. */
  readonly meshMaxParticipants: number;
  /** Cupo cuando quien agenda no indica uno. */
  readonly defaultMaxParticipants: number;
}

export const DEFAULT_MEETING_MEDIA_POLICY: MeetingMediaPolicy = {
  meshMaxParticipants: TECHNICAL_LIMITS.meshMaxParticipants.fallback,
  defaultMaxParticipants: TECHNICAL_LIMITS.maxParticipants.fallback,
};

/** Duración por defecto y máxima de la oficina (resolver). El máximo es el único que corta. */
export interface MeetingDurationPolicy {
  readonly defaultDurationMinutes: number;
  readonly maxDurationMinutes: number;
}

export const DEFAULT_MEETING_DURATION_POLICY: MeetingDurationPolicy = {
  defaultDurationMinutes: TECHNICAL_LIMITS.defaultMeetingDurationMinutes.fallback,
  maxDurationMinutes: TECHNICAL_LIMITS.maxMeetingDurationMinutes.fallback,
};

export interface ScheduleMeetingInput {
  readonly tenantId: string;
  readonly title: string;
  readonly description?: string | null;
  readonly host: { userId: string; displayName: string };
  readonly maxParticipants?: number;
  readonly requireWaitingRoom?: boolean;
  readonly passcodeHash?: string | null;
  readonly scheduledForUtc?: Date | null;
  readonly scheduledDurationMinutes?: number;
  readonly recordingRequested?: boolean;
  readonly transcriptLanguage?: string | null;
  /** D-69: valor inicial = el de la oficina; quien agenda puede apagarlo. */
  readonly guestsAllowed?: boolean;
  readonly mediaPolicy?: MeetingMediaPolicy;
  readonly durationPolicy?: MeetingDurationPolicy;
  readonly continuationOfMeetingId?: string | null;
  readonly appointmentId?: string | null;
  readonly now?: Date;
}

export type HostSuccession = { readonly outcome: 'succeeded'; readonly newHostUserId: string } | { readonly outcome: 'ended' };

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
    const policy = input.mediaPolicy ?? DEFAULT_MEETING_MEDIA_POLICY;
    const durations = input.durationPolicy ?? DEFAULT_MEETING_DURATION_POLICY;
    const maxParticipants = input.maxParticipants ?? policy.defaultMaxParticipants;
    if (maxParticipants < 2 || maxParticipants > 100) {
      return Result.fail(makeError('Meeting.InvalidMax', 'maxParticipants must be between 2 and 100.'));
    }
    const scheduledDurationMinutes = input.scheduledDurationMinutes ?? durations.defaultDurationMinutes;
    if (
      !Number.isInteger(scheduledDurationMinutes) ||
      scheduledDurationMinutes < 1 ||
      scheduledDurationMinutes > durations.maxDurationMinutes
    ) {
      return Result.fail(
        makeError('Meeting.InvalidDuration', `Duration must be between 1 and ${durations.maxDurationMinutes} minutes.`),
      );
    }
    const transcriptLanguage = input.transcriptLanguage?.trim() || null;
    if (transcriptLanguage !== null && !isTranscriptLanguage(transcriptLanguage)) {
      return Result.fail(makeError('Meeting.InvalidLanguage', 'transcriptLanguage must be null, "auto" or a language code.'));
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
      guestsAllowed: input.guestsAllowed ?? true,
      guestLinkEnabled: false,
      guestLinkVersion: 0,
      maxParticipants,
      strategy: maxParticipants <= policy.meshMaxParticipants ? MeetingStrategy.Mesh : MeetingStrategy.Sfu,
      recordingRequested: input.recordingRequested ?? false,
      recordingFileId: null,
      transcriptFileId: null,
      transcriptLanguage,
      hostUserId: input.host.userId,
      scheduledForUtc: input.scheduledForUtc ?? null,
      scheduledDurationMinutes,
      maxDurationMinutes: null,
      endsAtUtc: null,
      endReason: null,
      lastDurationWarningSeconds: null,
      reminderSentAtUtc: null,
      continuationOfMeetingId: input.continuationOfMeetingId ?? null,
      appointmentId: input.appointmentId ?? null,
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
    customerId?: string | null;
    hasValidInvitation: boolean;
    passcodeMatch: boolean | null;
    audioDefault?: boolean;
    videoDefault?: boolean;
    /** D-67: un invitado externo (sin cuenta) siempre espera a que lo admitan. */
    isGuest?: boolean;
    now?: Date;
  }): Result<{ requiresAdmission: boolean; role: MeetingRole; rejoined: boolean; handLoweredOnRejoin: boolean }> {
    const now = input.now ?? new Date();
    if (isTerminalMeetingStatus(this.state.status)) {
      return Result.fail(makeError('Meeting.NotJoinable', `Meeting is ${this.state.status}.`));
    }
    // El tope ya venció aunque el scheduler no haya cortado todavía: nadie entra (INV-11 §13.6).
    if (this.state.status === MeetingStatus.Live && this.state.endsAtUtc && now.getTime() >= this.state.endsAtUtc.getTime()) {
      return Result.fail(makeError('Meeting.Ended', 'Meeting reached its time limit.'));
    }
    if (this.state.isLocked && !input.hasValidInvitation && input.userId !== this.state.hostUserId) {
      return Result.fail(makeError('Meeting.Locked', 'Meeting is locked. Invitation required.'));
    }
    if (this.state.passcodeHash && input.passcodeMatch === false) {
      return Result.fail(makeError('Meeting.InvalidPasscode', 'Wrong passcode.'));
    }
    const existing = this.participants.find((p) => p.userId === input.userId);
    if (existing && existing.status === 'Joined') {
      // Sigue dentro pero vuelve a hacer join (recarga/otra pestaña): media nueva, joinOrder nuevo.
      // El host admitido por start() todavia no tenia media: su primera entrada por socket no es re-join.
      const firstSession = existing.isHostPlaceholderSession;
      // D-49: la mano levantada se baja con la sesion nueva y el cliente lo avisa.
      const handLoweredOnRejoin = existing.handRaised;
      const renewed = existing.startNewSession(this.nextJoinOrder());
      if (!renewed.isSuccess) return Result.fail(renewed.error);
      this.commit(now);
      return Result.ok({ requiresAdmission: false, role: existing.role, rejoined: !firstSession, handLoweredOnRejoin });
    }
    if (existing && existing.status === 'Waiting') {
      return Result.ok({ requiresAdmission: true, role: existing.role, rejoined: false, handLoweredOnRejoin: false });
    }
    // D-69: con los externos apagados no entra uno nuevo (los que ya estaban adentro siguen arriba).
    if (input.isGuest === true && !this.state.guestsAllowed) {
      return Result.fail(makeError('Meeting.GuestsNotAllowed', 'This meeting does not allow external guests.'));
    }

    const isHost = input.userId === this.state.hostUserId;
    const activeJoined = this.participants.filter((p) => p.isJoined).length;
    if (!isHost && activeJoined >= this.state.maxParticipants) {
      return Result.fail(makeError('Meeting.Full', 'Meeting is full.'));
    }

    // F11 (E-259): a quien el host sacó o rechazó no le basta con volver a pedir entrar: espera a que el
    // host lo admita de nuevo (antes volvía directo si la reunión no estaba bloqueada ni con sala de espera).
    const removedByHost = existing?.status === ParticipantStatus.Removed;
    const requiresAdmission = !isHost && (this.state.requireWaitingRoom || input.isGuest === true || removedByHost);
    const role: MeetingRole = isHost ? MeetingRole.Host : MeetingRole.Attendee;
    const joinOrder = this.nextJoinOrder();

    const participant = MeetingParticipant.create({
      meetingId: this.state.id,
      tenantId: this.state.tenantId,
      userId: input.userId,
      displayName: input.displayName,
      ...(input.actorType !== undefined ? { actorType: input.actorType } : {}),
      customerId: input.customerId ?? existing?.toSnapshot().customerId ?? null,
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
    return Result.ok({ requiresAdmission, role, rejoined: false, handLoweredOnRejoin: false });
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
    // "Leave" SOLO saca al participante: no cede el host ni termina el meeting. El host queda como
    // hostUserId (aunque Left) para poder volver y retomar/terminar; SOLO "End meeting" (end()) finaliza
    // para todos. Así salir y volver no mata la sala, y que todos hagan Leave tampoco la finaliza sola.
    // La sucesión del host la decide `succeedHost` cuando vence su gracia (D-08), no este método.
    this.commit(now);
    return Result.okVoid();
  }

  start(input: {
    hostUserId: string;
    /** Máximo efectivo de la oficina al arrancar (resolver); el corte es la duración elegida si es menor. */
    maxDurationMinutes?: number;
    audioDefault?: boolean;
    videoDefault?: boolean;
    now?: Date;
  }): Result<void> {
    if (this.state.status !== MeetingStatus.Scheduled) {
      return Result.fail(makeError('Meeting.InvalidTransition', `Cannot start from ${this.state.status}.`));
    }
    if (input.hostUserId !== this.state.hostUserId) {
      return Result.fail(makeError('Meeting.HostOnly', 'Only the host can start the meeting.'));
    }
    const now = input.now ?? new Date();
    // D-35 (revisada 2026-10-31): la duración elegida al crear es la que corta, dentro del máximo de la oficina.
    const ceiling = input.maxDurationMinutes ?? DEFAULT_MEETING_DURATION_POLICY.maxDurationMinutes;
    const scheduled = this.state.scheduledDurationMinutes;
    const maxDurationMinutes = scheduled !== null && scheduled > 0 ? Math.min(scheduled, ceiling) : ceiling;
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
      maxDurationMinutes,
      endsAtUtc: new Date(now.getTime() + maxDurationMinutes * 60_000),
      lastDurationWarningSeconds: null,
      reminderSentAtUtc: null,
      updatedAtUtc: now,
      participants: this.participants.map((p) => p.toSnapshot()),
    };
    return Result.okVoid();
  }

  end(input: { byUserId: string; now?: Date }): Result<void> {
    if (isTerminalMeetingStatus(this.state.status)) {
      return Result.fail(makeError('Meeting.AlreadyEnded', `Meeting already ${this.state.status}.`));
    }
    if (input.byUserId !== this.state.hostUserId && !this.isCohost(input.byUserId)) {
      return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can end the meeting.'));
    }
    this.applyEnded(input.now ?? new Date(), MeetingEndReason.Host);
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
      return Result.fail(
        makeError('Meeting.InvalidTransition', `Cannot reschedule from ${this.state.status}.`),
      );
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
   * La sala de una cita lleva el nombre de la cita: si en Calendar cambia ("Edit details"), cambia aquí.
   * Solo mientras no terminó; el mismo nombre no toca nada.
   */
  retitle(input: { title: string; now?: Date }): Result<void> {
    if (this.state.status !== MeetingStatus.Scheduled && this.state.status !== MeetingStatus.Live) {
      return Result.fail(makeError('Meeting.InvalidTransition', `Cannot rename when ${this.state.status}.`));
    }
    const title = input.title.trim().slice(0, 200);
    if (title.length === 0) return Result.fail(makeError('Meeting.MissingTitle', 'Title is required.'));
    if (title === this.state.title) return Result.okVoid();
    this.state = { ...this.state, title, updatedAtUtc: input.now ?? new Date() };
    return Result.okVoid();
  }

  /**
   * Host o cohost cambian el idioma del transcript mientras la reunión no terminó; se aplica al pedir el
   * transcript (al cerrar la grabación). null = automático.
   */
  setTranscriptLanguage(input: { byUserId: string; language: string | null; now?: Date }): Result<void> {
    if (this.state.status !== MeetingStatus.Scheduled && this.state.status !== MeetingStatus.Live) {
      return Result.fail(makeError('Meeting.InvalidTransition', `Cannot change the transcript language when ${this.state.status}.`));
    }
    if (input.byUserId !== this.state.hostUserId && !this.isCohost(input.byUserId)) {
      return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can change the transcript language.'));
    }
    const language = input.language?.trim() || null;
    if (language !== null && !isTranscriptLanguage(language)) {
      return Result.fail(makeError('Meeting.InvalidLanguage', 'transcriptLanguage must be null, "auto" or a language code.'));
    }
    this.state = { ...this.state, transcriptLanguage: language, updatedAtUtc: input.now ?? new Date() };
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

  /**
   * D-69: host o cohost abren o cierran la reunion a externos. Al cerrarla se rechaza a los externos que
   * esperaban (se devuelven para avisarles); los que ya estan adentro siguen.
   */
  setGuestAccess(input: { hostUserId: string; allowed: boolean; now?: Date }): Result<{ deniedUserIds: readonly string[] }> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return guard;
    const now = input.now ?? new Date();
    const denied = input.allowed
      ? []
      : this.participants.filter((p) => p.actorType === 'Guest' && p.status === ParticipantStatus.Waiting);
    denied.forEach((p) => p.remove(now));
    this.state = {
      ...this.state,
      guestsAllowed: input.allowed,
      // Sin externos el enlace no sirve: se apaga (encenderlo de nuevo genera uno nuevo).
      guestLinkEnabled: input.allowed ? this.state.guestLinkEnabled : false,
    };
    this.commit(now);
    return Result.ok({ deniedUserIds: denied.map((p) => p.userId) });
  }

  /**
   * D-68: enciende el enlace de la reunion. Encenderlo desde apagado o rotarlo sube la version, asi
   * que un enlace anterior ya no sirve.
   */
  enableGuestLink(input: { hostUserId: string; rotate: boolean; now?: Date }): Result<{ version: number }> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return guard;
    if (isTerminalMeetingStatus(this.state.status)) {
      return Result.fail(makeError('Meeting.NotJoinable', `Meeting is ${this.state.status}.`));
    }
    if (!this.state.guestsAllowed) {
      return Result.fail(makeError('Meeting.GuestsNotAllowed', 'This meeting does not allow external guests.'));
    }
    if (this.state.guestLinkEnabled && !input.rotate) return Result.ok({ version: this.state.guestLinkVersion });
    this.state = { ...this.state, guestLinkEnabled: true, guestLinkVersion: this.state.guestLinkVersion + 1 };
    this.commit(input.now ?? new Date());
    return Result.ok({ version: this.state.guestLinkVersion });
  }

  disableGuestLink(input: { hostUserId: string; now?: Date }): Result<void> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return guard;
    this.state = { ...this.state, guestLinkEnabled: false };
    this.commit(input.now ?? new Date());
    return Result.okVoid();
  }

  /** D-68: el enlace (de esa version) sigue sirviendo para pedir entrar. */
  acceptsGuestLink(version: number): boolean {
    return this.state.guestsAllowed && this.state.guestLinkEnabled && this.state.guestLinkVersion === version && !isTerminalMeetingStatus(this.state.status);
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
      return Result.fail(
        makeError('Meeting.CannotRemoveHost', 'Host cannot be removed. Transfer host first.'),
      );
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
    if (!next)
      return Result.fail(makeError('Meeting.TargetNotJoined', 'Target must be a joined participant.'));
    // D-09: un cliente o un guest nunca lleva la sala.
    if (!next.isStaff) return Result.fail(makeError('Meeting.Role.NotEligible', 'Only office staff can host.'));
    if (current) current.demoteHostToCohost();
    next.transferHost();
    const now = input.now ?? new Date();
    this.state = { ...this.state, hostUserId: input.newHostUserId, updatedAtUtc: now };
    this.commit(now);
    return Result.okVoid();
  }

  /**
   * Sucesión automática (D-08, T-06): el host venció su gracia sin reconectar. Gana el cohost Joined
   * más antiguo; si no hay, el staff Joined más antiguo. Sin staff dentro, la reunión termina por
   * sistema (NoParticipants). El host saliente queda como cohost: si vuelve, conserva sus controles.
   */
  succeedHost(input: { now?: Date } = {}): Result<HostSuccession> {
    if (this.state.status !== MeetingStatus.Live) {
      return Result.fail(makeError('Meeting.InvalidTransition', `Cannot succeed host from ${this.state.status}.`));
    }
    const host = this.participants.find((p) => p.userId === this.state.hostUserId);
    if (host?.isJoined) {
      return Result.fail(makeError('Meeting.HostPresent', 'The host is still in the meeting.'));
    }
    const now = input.now ?? new Date();
    const candidates = this.participants
      .filter((p) => p.isJoined && p.isStaff && p.userId !== this.state.hostUserId)
      .sort((a, b) => a.joinOrder - b.joinOrder);
    const successor = candidates.find((p) => p.role === MeetingRole.Cohost) ?? candidates[0];
    if (!successor) {
      this.applyEnded(now, MeetingEndReason.NoParticipants);
      return Result.ok({ outcome: 'ended' });
    }
    if (host) host.demoteHostToCohost();
    successor.transferHost();
    this.state = { ...this.state, hostUserId: successor.userId, updatedAtUtc: now };
    this.commit(now);
    return Result.ok({ outcome: 'succeeded', newHostUserId: successor.userId });
  }

  // ------------------------------------------------------------------
  // Handover por SISTEMA (offboarding de un empleado). Sin chequeo de actor:
  // el disparador es el consumer de `auth.user.offboarded.v1`, no una persona.
  // A diferencia de transferHost/cancel/end (solo el host), estos existen para
  // que una reunion cuyo host fue retirado no quede imposible de iniciar ni
  // cancelar por nadie.
  // ------------------------------------------------------------------

  /**
   * Pasa el host a `newHostUserId` sin exigir que este "joined" (una reunion
   * Scheduled no tiene a nadie adentro). Reusa su participante si ya existe, o
   * crea el placeholder de host igual que schedule(). El host saliente pierde
   * el rol y queda como salido (historial). Idempotente si ya es el host.
   */
  reassignHostBySystem(input: {
    newHostUserId: string;
    newHostDisplayName?: string;
    now?: Date;
  }): Result<void> {
    if (isTerminalMeetingStatus(this.state.status)) {
      return Result.fail(
        makeError('Meeting.InvalidTransition', `Cannot reassign host from ${this.state.status}.`),
      );
    }
    const now = input.now ?? new Date();
    if (input.newHostUserId === this.state.hostUserId) return Result.okVoid();

    const previousHostId = this.state.hostUserId;
    const previous = this.participants.find((p) => p.userId === previousHostId);
    const next = this.participants.find((p) => p.userId === input.newHostUserId);
    if (next) {
      next.transferHost(); // conserva su status (si estaba Joined en un Live, sigue Joined)
    } else {
      this.participants.push(
        MeetingParticipant.create({
          meetingId: this.state.id,
          tenantId: this.state.tenantId,
          userId: input.newHostUserId,
          displayName: (input.newHostDisplayName ?? 'Host').trim() || 'Host',
          role: MeetingRole.Host,
          status: ParticipantStatus.Left, // entra al start()/join(), igual que el placeholder de schedule()
          joinOrder: this.nextJoinOrder(),
          audioDefault: true,
          videoDefault: true,
          now,
        }),
      );
    }
    if (previous) {
      previous.demoteHostToCohost(); // quita el rol Host...
      previous.demoteToAttendee(); // ...hasta Attendee (ya no es host ni cohost)
      previous.markLeft(now); // y lo saca de la sala
    }
    this.state = { ...this.state, hostUserId: input.newHostUserId, updatedAtUtc: now };
    this.commit(now);
    return Result.okVoid();
  }

  /** Cancela por sistema una reunion Scheduled cuyo host se retiro y no hay sucesor. */
  cancelBySystem(input: { now?: Date } = {}): Result<void> {
    if (this.state.status !== MeetingStatus.Scheduled) {
      return Result.fail(makeError('Meeting.InvalidTransition', `Cannot cancel from ${this.state.status}.`));
    }
    const now = input.now ?? new Date();
    this.state = { ...this.state, status: MeetingStatus.Cancelled, endedAtUtc: now, updatedAtUtc: now };
    return Result.okVoid();
  }

  /**
   * Retiro de un empleado que figura en una reunion agendada de OTRO host (incluido el host viejo que
   * `reassignHostBySystem` dejo como Attendee): queda Removed y deja de recibir avisos. El host no se
   * quita por aca (lo resuelve el handover). Idempotente: `changed=false` si no estaba o ya salio.
   */
  removeParticipantBySystem(input: { userId: string; now?: Date }): Result<{ changed: boolean }> {
    if (this.state.status !== MeetingStatus.Scheduled) {
      return Result.fail(makeError('Meeting.InvalidTransition', `Cannot remove a participant from ${this.state.status}.`));
    }
    if (input.userId === this.state.hostUserId) {
      return Result.fail(makeError('Meeting.CannotRemoveHost', 'Host cannot be removed. Transfer host first.'));
    }
    const target = this.participants.find((p) => p.userId === input.userId);
    if (!target || target.status === ParticipantStatus.Removed) return Result.ok({ changed: false });
    const now = input.now ?? new Date();
    target.remove(now);
    this.commit(now);
    return Result.ok({ changed: true });
  }

  /** Termina por sistema una reunion Live con el motivo explícito (tope, sala vacía, offboarding, backfill). */
  endBySystem(input: { reason: Exclude<MeetingEndReason, 'Host'>; now?: Date }): Result<void> {
    if (isTerminalMeetingStatus(this.state.status)) {
      return Result.fail(makeError('Meeting.AlreadyEnded', `Meeting already ${this.state.status}.`));
    }
    this.applyEnded(input.now ?? new Date(), input.reason);
    return Result.okVoid();
  }

  /** Agendada que nunca empezó y venció (D-35): pasa a Expired, terminal. */
  expire(input: { now?: Date } = {}): Result<void> {
    if (this.state.status !== MeetingStatus.Scheduled) {
      return Result.fail(makeError('Meeting.InvalidTransition', `Cannot expire from ${this.state.status}.`));
    }
    const now = input.now ?? new Date();
    this.state = { ...this.state, status: MeetingStatus.Expired, endedAtUtc: now, updatedAtUtc: now };
    return Result.okVoid();
  }

  /**
   * Registra que se emitió el aviso de un umbral (10/5/1 min). Idempotente: cada umbral se avisa una
   * sola vez y solo en orden descendente, aunque el scheduler pase varias veces.
   */
  /** F6 (T-64): el recordatorio se manda una sola vez por reunion agendada. */
  markReminderSent(input: { now?: Date } = {}): Result<void> {
    if (this.state.status !== MeetingStatus.Scheduled) {
      return Result.fail(makeError('Meeting.InvalidTransition', 'Only a scheduled meeting gets a reminder.'));
    }
    if (this.state.reminderSentAtUtc !== null) {
      return Result.fail(makeError('Meeting.NoOp', 'Reminder was already sent.'));
    }
    const now = input.now ?? new Date();
    this.state = { ...this.state, reminderSentAtUtc: now, updatedAtUtc: now };
    return Result.okVoid();
  }

  markDurationWarning(input: { thresholdSeconds: number; now?: Date }): Result<void> {
    if (this.state.status !== MeetingStatus.Live) {
      return Result.fail(makeError('Meeting.InvalidTransition', 'Only a live meeting gets duration warnings.'));
    }
    const last = this.state.lastDurationWarningSeconds;
    if (last !== null && input.thresholdSeconds >= last) {
      return Result.fail(makeError('Meeting.NoOp', 'This warning was already sent.'));
    }
    this.state = {
      ...this.state,
      lastDurationWarningSeconds: input.thresholdSeconds,
      updatedAtUtc: input.now ?? new Date(),
    };
    return Result.okVoid();
  }

  /** Segundos que faltan para el corte; null si no está Live o no tiene tope fijado. */
  remainingSeconds(now: Date = new Date()): number | null {
    if (this.state.status !== MeetingStatus.Live || !this.state.endsAtUtc) return null;
    return Math.max(0, Math.floor((this.state.endsAtUtc.getTime() - now.getTime()) / 1000));
  }

  /** Desde cuándo no queda nadie Joined en una reunión Live; null si hay alguien dentro. */
  emptySinceUtc(): Date | null {
    if (this.state.status !== MeetingStatus.Live) return null;
    if (this.participants.some((p) => p.isJoined)) return null;
    let since = this.state.startedAtUtc ?? this.state.createdAtUtc;
    for (const p of this.participants) {
      if (p.leftAtUtc && p.leftAtUtc.getTime() > since.getTime()) since = p.leftAtUtc;
    }
    return since;
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

  raiseHand(input: { byUserId: string; raised: boolean; now?: Date }): Result<void> {
    const participant = this.participants.find((p) => p.userId === input.byUserId && p.isJoined);
    if (!participant) return Result.fail(makeError('Meeting.NotParticipant', 'Not a joined participant.'));
    const now = input.now ?? new Date();
    participant.setHandRaised(input.raised, now);
    this.commit(now);
    return Result.okVoid();
  }

  /** F5 (D-49): host/cohost baja la mano de un participante. Idempotente: dice si cambio algo. */
  lowerHand(input: { hostUserId: string; targetUserId: string; now?: Date }): Result<{ changed: boolean }> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return Result.fail(guard.error);
    const target = this.participants.find((p) => p.userId === input.targetUserId && p.isJoined);
    if (!target) return Result.fail(makeError('Meeting.NotFound', 'Target participant not found.'));
    if (!target.handRaised) return Result.ok({ changed: false });
    const now = input.now ?? new Date();
    target.setHandRaised(false, now);
    this.commit(now);
    return Result.ok({ changed: true });
  }

  /** F5 (D-49): baja todas las manos levantadas; devuelve los userIds afectados. */
  lowerAllHands(input: { hostUserId: string; now?: Date }): Result<{ lowered: readonly string[] }> {
    const guard = this.ensureCanHostAct(input.hostUserId);
    if (!guard.isSuccess) return Result.fail(guard.error);
    const now = input.now ?? new Date();
    const lowered: string[] = [];
    for (const p of this.participants) {
      if (!p.isJoined || !p.handRaised) continue;
      p.setHandRaised(false, now);
      lowered.push(p.userId);
    }
    if (lowered.length > 0) this.commit(now);
    return Result.ok({ lowered });
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
        makeError(
          'Meeting.Recording.InvalidState',
          `Cannot attach recording in status ${this.state.status}.`,
        ),
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
  /**
   * F7: archivo de la grabacion server-side (compuesto). A diferencia de `attachRecording` (cliente,
   * una sola vez), un recompuesto reemplaza al anterior: la fuente de verdad es la RecordingSession.
   */
  setRecordingFile(fileId: string): Result<void> {
    if (this.state.status !== MeetingStatus.Ended && this.state.status !== MeetingStatus.Live) {
      return Result.fail(makeError('Meeting.Recording.InvalidState', `Cannot set the recording in status ${this.state.status}.`));
    }
    this.state = { ...this.state, recordingFileId: fileId, updatedAtUtc: new Date() };
    return Result.okVoid();
  }

  /** F7: transcript v2 (o reproceso) reemplaza al anterior; el estado vive en TranscriptArtifact. */
  setTranscriptFile(fileId: string): Result<void> {
    if (this.state.status !== MeetingStatus.Ended && this.state.status !== MeetingStatus.Live) {
      return Result.fail(makeError('Meeting.Transcript.InvalidState', `Cannot set the transcript in status ${this.state.status}.`));
    }
    this.state = { ...this.state, transcriptFileId: fileId, updatedAtUtc: new Date() };
    return Result.okVoid();
  }

  /** F7 (T-37): la retencion borro los archivos de una grabacion; solo se sueltan si son los que se muestran (D-71). */
  clearRecordingFiles(files: { recordingFileId: string | null; transcriptFileId: string | null }): void {
    const recordingFileId = files.recordingFileId !== null && this.state.recordingFileId === files.recordingFileId ? null : this.state.recordingFileId;
    const transcriptFileId = files.transcriptFileId !== null && this.state.transcriptFileId === files.transcriptFileId ? null : this.state.transcriptFileId;
    this.state = { ...this.state, recordingFileId, transcriptFileId, updatedAtUtc: new Date() };
  }

  attachTranscript(fileId: string): Result<void> {
    if (this.state.status !== MeetingStatus.Ended && this.state.status !== MeetingStatus.Live) {
      return Result.fail(
        makeError(
          'Meeting.Transcript.InvalidState',
          `Cannot attach transcript in status ${this.state.status}.`,
        ),
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

  /** D-71: se puede grabar varias veces; solo se rechaza mientras la ultima grabacion sigue capturando. */
  requestRecording(input: {
    actorUserId: string;
    latestSession: RecordingSession | null;
    now?: Date;
  }): Result<{ session: RecordingSession; participantUserIds: readonly string[] }> {
    const guard = this.ensureCanHostAct(input.actorUserId);
    if (!guard.isSuccess) return guard;
    if (input.latestSession?.isCapturing) {
      return Result.fail(makeError('Meeting.Recording.InProgress', 'This meeting is already being recorded.'));
    }
    const sessionResult = RecordingSession.request({
      tenantId: this.state.tenantId,
      scope: RecordingScope.Meeting,
      scopeId: this.state.id,
      requestedByUserId: input.actorUserId,
      sequence: (input.latestSession?.sequence ?? 0) + 1,
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
    captureMode?: RecordingCaptureMode;
    maxDurationSeconds?: number | null;
  }): Result<RecordingSession> {
    const guard = this.ensureCanHostAct(input.actorUserId);
    if (!guard.isSuccess) return guard;
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.start({
      policyAllows: input.policyAllows,
      now: input.now ?? new Date(),
      ...(input.captureMode ? { captureMode: input.captureMode } : {}),
      maxDurationSeconds: input.maxDurationSeconds ?? null,
    });
    if (!result.isSuccess) return result;
    return Result.ok(input.session);
  }

  stopRecording(input: {
    actorUserId: string;
    session: RecordingSession;
    now?: Date;
    reason?: RecordingStopReason;
  }): Result<RecordingSession> {
    const guard = this.ensureCanHostAct(input.actorUserId);
    if (!guard.isSuccess) return guard;
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.stop(input.now, input.reason);
    if (!result.isSuccess) return result;
    return Result.ok(input.session);
  }

  /**
   * Stopping -> Processing, disparado por attach-meeting-recording.ts al
   * recibir el fileId. Sin chequeo de actor propio — el caller (Fase Backend 3)
   * ya valida Host/Cohost antes de llegar aca, igual que failRecording/completeRecording.
   */
  beginProcessingRecording(input: { session: RecordingSession; recordingFileId?: string }): Result<RecordingSession> {
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.beginProcessing(input.recordingFileId);
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
  /** F7 (T-34): el archivo de la grabacion ya esta Available en CloudStorage — sin chequeo de actor (sistema). */
  markRecordingReady(input: { session: RecordingSession; now?: Date }): Result<RecordingSession> {
    const belongs = this.ensureSessionBelongsHere(input.session);
    if (!belongs.isSuccess) return belongs;
    const result = input.session.markReady({ now: input.now ?? new Date() });
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
  get endsAtUtc(): Date | null {
    return this.state.endsAtUtc;
  }

  getJoinedParticipants(): readonly MeetingParticipantSnapshot[] {
    return this.participants.filter((p) => p.isJoined).map((p) => p.toSnapshot());
  }

  isJoinedParticipant(userId: string): boolean {
    return this.participants.some((p) => p.userId === userId && p.isJoined);
  }

  /** Participó alguna vez (cualquier estado) o es el host: quién puede leer la ficha/eventos. */
  isKnownParticipant(userId: string): boolean {
    return userId === this.state.hostUserId || this.participants.some((p) => p.userId === userId);
  }

  /** A quién se avisa de la reunión agendada (recordatorio, cambio, cancelación): todos menos los Removed. */
  notifiableUserIds(): string[] {
    return this.participants.filter((p) => p.status !== ParticipantStatus.Removed).map((p) => p.userId);
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

  private nextJoinOrder(): number {
    return this.participants.reduce((max, p) => Math.max(max, p.joinOrder), 0) + 1;
  }

  /** Transicion a Ended compartida por end() (host/cohost), endBySystem() y la sucesión sin staff. */
  private applyEnded(now: Date, reason: MeetingEndReason): void {
    this.participants.forEach((p) => {
      if (p.status === 'Joined' || p.status === 'Waiting') p.markLeft(now);
    });
    const startedAt = this.state.startedAtUtc;
    this.state = {
      ...this.state,
      status: MeetingStatus.Ended,
      endedAtUtc: now,
      endReason: reason,
      durationSeconds: startedAt ? Math.max(0, Math.floor((now.getTime() - startedAt.getTime()) / 1000)) : 0,
      updatedAtUtc: now,
      participants: this.participants.map((p) => p.toSnapshot()),
    };
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
