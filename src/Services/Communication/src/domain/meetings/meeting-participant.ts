import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import { isStaffActor } from '../shared/permissions.js';
import type { MeetingRole, ParticipantStatus } from './meeting-enums.js';
import type { ConnectionQuality, MediaStatus } from '../calls/media-status.js';

export interface MeetingParticipantSnapshot {
  readonly id: string;
  readonly meetingId: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly displayName: string;
  // Espejo libre del claim JWT `actor_type` ("TenantEmployee"|"TenantAdmin"|
  // "Customer"|...) — Fase Backend 5 agrega "Guest" para invitados sin cuenta
  // Auth (ver join-meeting.ts). Nunca gatea autorizacion por si solo — eso lo
  // hace `role` (Host/Cohost/Attendee) + `hasPermission` sobre el principal
  // del socket, que para un Guest siempre trae permissions=[].
  readonly actorType: string;
  /** D-58: cliente de la oficina detrás de un usuario del portal; null para staff y guests. */
  readonly customerId: string | null;
  readonly role: MeetingRole;
  readonly status: ParticipantStatus;
  readonly joinOrder: number;
  readonly requestedAtUtc: Date;
  readonly admittedAtUtc: Date | null;
  readonly joinedAtUtc: Date | null;
  readonly leftAtUtc: Date | null;
  readonly audioEnabled: boolean;
  readonly videoEnabled: boolean;
  readonly screenSharing: boolean;
  readonly handRaised: boolean;
  /** F5 (D-49): cuando levanto la mano; null = baja. Ordena la cola del host. */
  readonly handRaisedAtUtc: Date | null;
  readonly connectionQuality: ConnectionQuality;
}

export class MeetingParticipant {
  private constructor(private state: MeetingParticipantSnapshot) {}

  static rehydrate(snapshot: MeetingParticipantSnapshot): MeetingParticipant {
    return new MeetingParticipant(snapshot);
  }

  static create(input: {
    meetingId: string;
    tenantId: string;
    userId: string;
    displayName: string;
    actorType?: string;
    customerId?: string | null;
    role: MeetingRole;
    status: ParticipantStatus;
    joinOrder: number;
    audioDefault: boolean;
    videoDefault: boolean;
    now: Date;
  }): MeetingParticipant {
    return new MeetingParticipant({
      id: randomUUID(),
      meetingId: input.meetingId,
      tenantId: input.tenantId,
      userId: input.userId,
      displayName: input.displayName.trim().slice(0, 120),
      actorType: input.actorType ?? 'TenantEmployee',
      customerId: input.customerId ?? null,
      role: input.role,
      status: input.status,
      joinOrder: input.joinOrder,
      requestedAtUtc: input.now,
      admittedAtUtc: input.status === 'Joined' ? input.now : null,
      joinedAtUtc: input.status === 'Joined' ? input.now : null,
      leftAtUtc: null,
      audioEnabled: input.audioDefault,
      videoEnabled: input.videoDefault,
      screenSharing: false,
      handRaised: false,
      handRaisedAtUtc: null,
      connectionQuality: 'Unknown',
    });
  }

  admit(now: Date): Result<void> {
    if (this.state.status !== 'Waiting') {
      return Result.fail(makeError('Meeting.Participant.NotWaiting', `Cannot admit a ${this.state.status} participant.`));
    }
    this.state = { ...this.state, status: 'Joined', admittedAtUtc: now, joinedAtUtc: now };
    return Result.okVoid();
  }

  markLeft(now: Date): void {
    if (this.state.status === 'Left' || this.state.status === 'Removed') return;
    this.state = { ...this.state, status: 'Left', leftAtUtc: now, handRaised: false, handRaisedAtUtc: null };
  }

  /**
   * Re-join de un participante que sigue Joined (recarga de pagina, otra pestaña): su media anterior
   * murio, asi que recibe un joinOrder nuevo. Los peers rearman el PeerConnection al ver uno mayor.
   * No toca status ni joinedAtUtc: la permanencia en la reunion no se interrumpio.
   */
  startNewSession(joinOrder: number): Result<void> {
    if (this.state.status !== 'Joined') {
      return Result.fail(makeError('Meeting.Participant.NotJoined', 'Only a joined participant can start a new session.'));
    }
    if (joinOrder <= this.state.joinOrder) {
      return Result.fail(makeError('Meeting.Participant.StaleJoinOrder', 'joinOrder must increase.'));
    }
    // D-49: el re-join completo baja la mano (la sesion anterior murio); la reconexion transparente no pasa por aqui.
    this.state = { ...this.state, joinOrder, handRaised: false, handRaisedAtUtc: null };
    return Result.okVoid();
  }

  remove(now: Date): void {
    if (this.state.status === 'Removed') return;
    this.state = { ...this.state, status: 'Removed', leftAtUtc: now, handRaised: false, handRaisedAtUtc: null };
  }

  applyMediaStatus(status: MediaStatus): void {
    if (this.state.status !== 'Joined') return;
    this.state = {
      ...this.state,
      audioEnabled: status.audioEnabled,
      videoEnabled: status.videoEnabled,
      screenSharing: status.screenSharing,
    };
  }

  forceMuteAudio(): void {
    if (this.state.status !== 'Joined') return;
    this.state = { ...this.state, audioEnabled: false };
  }

  setHandRaised(value: boolean, now: Date = new Date()): void {
    if (this.state.status !== 'Joined' || value === this.state.handRaised) return;
    this.state = { ...this.state, handRaised: value, handRaisedAtUtc: value ? now : null };
  }

  /** D-09: solo el personal de la oficina puede ser cohost u host; clientes y guests nunca. */
  promoteToCohost(): Result<void> {
    if (this.state.role === 'Host') {
      return Result.fail(makeError('Meeting.Role.HostCannotBeCohost', 'Host cannot be cohost.'));
    }
    if (!this.isStaff) {
      return Result.fail(makeError('Meeting.Role.NotEligible', 'Only office staff can be a co-host.'));
    }
    this.state = { ...this.state, role: 'Cohost' };
    return Result.okVoid();
  }

  demoteToAttendee(): Result<void> {
    if (this.state.role === 'Host') {
      return Result.fail(makeError('Meeting.Role.CannotDemoteHost', 'Cannot demote host.'));
    }
    this.state = { ...this.state, role: 'Attendee' };
    return Result.okVoid();
  }

  transferHost(): void {
    this.state = { ...this.state, role: 'Host' };
  }

  demoteHostToCohost(): void {
    this.state = { ...this.state, role: 'Cohost' };
  }

  updateConnectionQuality(quality: ConnectionQuality): void {
    if (this.state.status !== 'Joined') return;
    this.state = { ...this.state, connectionQuality: quality };
  }

  toSnapshot(): MeetingParticipantSnapshot {
    return this.state;
  }

  get userId(): string {
    return this.state.userId;
  }
  get displayName(): string {
    return this.state.displayName;
  }
  get actorType(): string {
    return this.state.actorType;
  }
  get role(): MeetingRole {
    return this.state.role;
  }
  get status(): ParticipantStatus {
    return this.state.status;
  }
  get isJoined(): boolean {
    return this.state.status === 'Joined';
  }
  get joinOrder(): number {
    return this.state.joinOrder;
  }
  get leftAtUtc(): Date | null {
    return this.state.leftAtUtc;
  }
  get handRaised(): boolean {
    return this.state.handRaised;
  }
  /** Personal de la oficina (empleado/admin): el único elegible para host o cohost (D-08/D-09). */
  get isStaff(): boolean {
    return isStaffActor(this.state.actorType);
  }
  /**
   * `Meeting.start()` deja al host Joined sin media y con el joinOrder 1 de `schedule()`: es un
   * placeholder. Su primer `join` por socket renueva la sesion pero cuenta como entrada, no como re-join.
   */
  get isHostPlaceholderSession(): boolean {
    return this.state.role === 'Host' && this.state.status === 'Joined' && this.state.joinOrder === 1;
  }
}
