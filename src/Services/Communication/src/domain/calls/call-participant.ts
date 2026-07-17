import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import type { ConnectionQuality, MediaStatus } from './media-status.js';

export interface CallParticipantSnapshot {
  readonly id: string;
  readonly callId: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly displayName: string;
  readonly role: 'Caller' | 'Callee';
  /**
   * Monotonic asignado por el server al join. Perfect negotiation regla del
   * plan §9C: el que tenga JoinOrder MAYOR es el peer "polite".
   */
  readonly joinOrder: number;
  readonly joinedAtUtc: Date;
  readonly leftAtUtc: Date | null;
  readonly audioEnabled: boolean;
  readonly videoEnabled: boolean;
  readonly screenSharing: boolean;
  /**
   * Fase Backend 7 — timestamp del comienzo del screen share actual. Se
   * setea al llamar startScreenSharing() y se limpia al llamar
   * stopScreenSharing(). Sirve para calcular `durationSeconds` en el evento
   * de integracion `CallScreenShareStoppedEvent` sin depender de logs.
   */
  readonly screenShareStartedAtUtc: Date | null;
  readonly connectionQuality: ConnectionQuality;
}

export class CallParticipant {
  private constructor(private state: CallParticipantSnapshot) {}

  static rehydrate(snapshot: CallParticipantSnapshot): CallParticipant {
    return new CallParticipant(snapshot);
  }

  static create(input: {
    callId: string;
    tenantId: string;
    userId: string;
    displayName: string;
    role: 'Caller' | 'Callee';
    joinOrder: number;
    audioDefault: boolean;
    videoDefault: boolean;
    now: Date;
  }): CallParticipant {
    return new CallParticipant({
      id: randomUUID(),
      callId: input.callId,
      tenantId: input.tenantId,
      userId: input.userId,
      displayName: input.displayName.trim().slice(0, 120),
      role: input.role,
      joinOrder: input.joinOrder,
      joinedAtUtc: input.now,
      leftAtUtc: null,
      audioEnabled: input.audioDefault,
      videoEnabled: input.videoDefault,
      screenSharing: false,
      screenShareStartedAtUtc: null,
      connectionQuality: 'Unknown',
    });
  }

  applyMediaStatus(actorUserId: string, status: MediaStatus): Result<void> {
    if (actorUserId !== this.state.userId) {
      return Result.fail(
        makeError('Call.MediaStatus.Forbidden', 'Only the participant can update their own media status.'),
      );
    }
    if (this.state.leftAtUtc !== null) {
      return Result.fail(makeError('Call.Participant.AlreadyLeft', 'Participant already left the call.'));
    }
    this.state = {
      ...this.state,
      audioEnabled: status.audioEnabled,
      videoEnabled: status.videoEnabled,
      screenSharing: status.screenSharing,
    };
    return Result.okVoid();
  }

  /**
   * Fase Backend 7 — inicia el screen share con un timestamp propio (no via
   * applyMediaStatus, que ya se usa para audio/video toggle). El aggregate
   * valida que solo un participante comparta a la vez; aca solo se garantiza
   * que este participante no ya este compartiendo y que no haya salido.
   */
  startScreenSharing(now: Date): Result<void> {
    if (this.state.leftAtUtc !== null) {
      return Result.fail(makeError('Call.Participant.AlreadyLeft', 'Participant already left the call.'));
    }
    if (this.state.screenSharing) {
      return Result.fail(makeError('Call.ScreenShare.AlreadySharing', 'This participant is already screen sharing.'));
    }
    this.state = { ...this.state, screenSharing: true, screenShareStartedAtUtc: now };
    return Result.okVoid();
  }

  stopScreenSharing(): Result<{ startedAtUtc: Date }> {
    if (!this.state.screenSharing || this.state.screenShareStartedAtUtc === null) {
      return Result.fail(makeError('Call.ScreenShare.NotSharing', 'This participant is not currently screen sharing.'));
    }
    const startedAtUtc = this.state.screenShareStartedAtUtc;
    this.state = { ...this.state, screenSharing: false, screenShareStartedAtUtc: null };
    return Result.ok({ startedAtUtc });
  }

  get screenSharing(): boolean {
    return this.state.screenSharing;
  }

  updateConnectionQuality(quality: ConnectionQuality): void {
    if (this.state.leftAtUtc !== null) return;
    this.state = { ...this.state, connectionQuality: quality };
  }

  markLeft(now: Date): void {
    if (this.state.leftAtUtc !== null) return;
    this.state = { ...this.state, leftAtUtc: now };
  }

  toSnapshot(): CallParticipantSnapshot {
    return this.state;
  }

  get userId(): string {
    return this.state.userId;
  }
  get role(): 'Caller' | 'Callee' {
    return this.state.role;
  }
  get hasLeft(): boolean {
    return this.state.leftAtUtc !== null;
  }
  get joinOrder(): number {
    return this.state.joinOrder;
  }
}
