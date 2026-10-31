import { randomBytes, randomUUID, createHash } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';

export const MeetingInviteeKind = {
  Employee: 'Employee',
  Customer: 'Customer',
  External: 'External',
} as const;
export type MeetingInviteeKind = (typeof MeetingInviteeKind)[keyof typeof MeetingInviteeKind];

export function isMeetingInviteeKind(value: string): value is MeetingInviteeKind {
  return value === 'Employee' || value === 'Customer' || value === 'External';
}

/** Motivo por el que la reunion cerro la invitacion (F6). */
export type InvitationCloseReason = 'Ended' | 'Cancelled' | 'Expired';

/**
 * Invitacion a un meeting. Genera un token opaco cripto (32 bytes hex = 64
 * chars) y guarda solo su SHA-256 hex (128 chars). El token en claro se
 * devuelve UNA sola vez al creador — cierre CRIT-6 legacy.
 *
 * F6 (D-48): un invitado CON cuenta (Employee/Customer) reutiliza su token hasta que la invitacion
 * expira, se revoca o la reunion cierra (`recordUse` deja rastro: primer/ultimo uso y conteo). Un
 * guest (External) sigue siendo de un solo uso (`UsedAtUtc`), salvo el re-join mientras su
 * participante sigue dentro (lo decide join-meeting.ts).
 */
export interface MeetingInvitationSnapshot {
  readonly id: string;
  readonly meetingId: string;
  readonly tenantId: string;
  readonly inviteeKind: MeetingInviteeKind;
  readonly inviteeEmail: string | null;
  readonly inviteeUserId: string | null;
  readonly inviteeName: string | null;
  readonly inviteeExternalPhone: string | null;
  readonly tokenHash: string;
  readonly expiresAtUtc: Date;
  /** Guest: consumido (un solo uso). Usuarios con cuenta: nunca se fija. */
  readonly usedAtUtc: Date | null;
  readonly revokedAtUtc: Date | null;
  readonly firstUsedAtUtc: Date | null;
  readonly lastUsedAtUtc: Date | null;
  readonly useCount: number;
  /** La reunion termino/se cancelo/expiro con la invitacion abierta. */
  readonly closedAtUtc: Date | null;
  readonly createdAtUtc: Date;
}

export interface InvitationIssueResult {
  readonly invitation: MeetingInvitation;
  readonly plainToken: string;
}

export class MeetingInvitation {
  private constructor(private state: MeetingInvitationSnapshot) {}

  static rehydrate(snapshot: MeetingInvitationSnapshot): MeetingInvitation {
    return new MeetingInvitation(snapshot);
  }

  static issue(input: {
    meetingId: string;
    tenantId: string;
    inviteeKind: MeetingInviteeKind;
    inviteeEmail?: string | null;
    inviteeUserId?: string | null;
    inviteeName?: string | null;
    inviteeExternalPhone?: string | null;
    ttlSeconds: number;
    now: Date;
  }): Result<InvitationIssueResult> {
    if (!input.inviteeEmail && !input.inviteeUserId) {
      return Result.fail(
        makeError('Meeting.Invitation.NoInvitee', 'Either inviteeEmail or inviteeUserId is required.'),
      );
    }
    const plainToken = randomBytes(32).toString('hex');
    const tokenHash = MeetingInvitation.hash(plainToken);
    const snapshot: MeetingInvitationSnapshot = {
      id: randomUUID(),
      meetingId: input.meetingId,
      tenantId: input.tenantId,
      inviteeKind: input.inviteeKind,
      inviteeEmail: input.inviteeEmail ?? null,
      inviteeUserId: input.inviteeUserId ?? null,
      inviteeName: input.inviteeName ?? null,
      inviteeExternalPhone: input.inviteeExternalPhone ?? null,
      tokenHash,
      expiresAtUtc: new Date(input.now.getTime() + input.ttlSeconds * 1000),
      usedAtUtc: null,
      revokedAtUtc: null,
      firstUsedAtUtc: null,
      lastUsedAtUtc: null,
      useCount: 0,
      closedAtUtc: null,
      createdAtUtc: input.now,
    };
    return Result.ok({ invitation: new MeetingInvitation(snapshot), plainToken });
  }

  static hash(plainToken: string): string {
    return createHash('sha256').update(plainToken).digest('hex');
  }

  /** Estado sin mirar el token: revocada, cerrada por la reunion, vencida o (guest) ya consumida. */
  private checkOpen(now: Date): Result<void> {
    if (this.state.revokedAtUtc !== null) {
      return Result.fail(makeError('Meeting.Invitation.Revoked', 'Invitation was revoked.'));
    }
    if (this.state.closedAtUtc !== null) {
      return Result.fail(makeError('Meeting.Invitation.Closed', 'The meeting is over.'));
    }
    if (now.getTime() > this.state.expiresAtUtc.getTime()) {
      return Result.fail(makeError('Meeting.Invitation.Expired', 'Invitation expired.'));
    }
    if (this.state.inviteeKind === 'External' && this.state.usedAtUtc !== null) {
      return Result.fail(makeError('Meeting.Invitation.AlreadyUsed', 'Invitation was already used.'));
    }
    return Result.okVoid();
  }

  validateForUse(input: { plainToken: string; now: Date }): Result<void> {
    const open = this.checkOpen(input.now);
    if (!open.isSuccess) return open;
    if (MeetingInvitation.hash(input.plainToken) !== this.state.tokenHash) {
      return Result.fail(makeError('Meeting.Invitation.InvalidToken', 'Invalid invitation token.'));
    }
    return Result.okVoid();
  }

  /**
   * F6 (D-48): un guest que ya consumio su unico uso puede volver a entrar mientras su participante siga
   * dentro (gracia) o en la sala de espera: recarga de pagina, no un segundo uso. `participantStatus` =
   * estado actual de `guestUserId` en la reunion (null si no esta).
   */
  allowsGuestRejoin(participantStatus: string | null): boolean {
    return (
      this.state.inviteeKind === 'External' &&
      this.state.usedAtUtc !== null &&
      this.state.revokedAtUtc === null &&
      this.state.closedAtUtc === null &&
      (participantStatus === 'Joined' || participantStatus === 'Waiting')
    );
  }

  /** Abierta ahora mismo (sin verificar el token): lo que una lista de host llama "active". */
  isOpen(now: Date): boolean {
    return this.checkOpen(now).isSuccess;
  }

  /**
   * Fase Backend 5 — usado por join-meeting.ts en el flujo de guest (socket
   * `auth.ticket`). A esta altura el token en claro ya no esta disponible (el
   * ticket solo lleva `invitationId`, ver resolve-invitation-token.ts) — la
   * validacion criptografica del token ya ocurrio una vez al mintear el
   * ticket. Aca solo se re-chequea el estado (defensa en profundidad contra
   * una revocacion que ocurrio mientras el guest tenia el ticket en la mano)
   * y se consume atomicamente.
   */
  consumeForGuestJoin(now: Date): Result<void> {
    const open = this.checkOpen(now);
    if (!open.isSuccess) return open;
    this.state = {
      ...this.state,
      usedAtUtc: now,
      firstUsedAtUtc: this.state.firstUsedAtUtc ?? now,
      lastUsedAtUtc: now,
      useCount: this.state.useCount + 1,
    };
    return Result.okVoid();
  }

  /** F6 (D-48): uso de un invitado con cuenta; no consume, solo deja rastro. */
  recordUse(now: Date): void {
    this.state = {
      ...this.state,
      firstUsedAtUtc: this.state.firstUsedAtUtc ?? now,
      lastUsedAtUtc: now,
      useCount: this.state.useCount + 1,
    };
  }

  revoke(now: Date): void {
    this.state = { ...this.state, revokedAtUtc: now };
  }

  /** F6: la reunion termino/se cancelo/expiro; solo cierra las que seguian abiertas. Devuelve si cambio. */
  close(now: Date): boolean {
    if (this.state.closedAtUtc !== null || this.state.revokedAtUtc !== null) return false;
    if (this.state.inviteeKind === 'External' && this.state.usedAtUtc !== null) return false;
    this.state = { ...this.state, closedAtUtc: now };
    return true;
  }

  toSnapshot(): MeetingInvitationSnapshot {
    return this.state;
  }

  get id(): string {
    return this.state.id;
  }
  get inviteeKind(): MeetingInviteeKind {
    return this.state.inviteeKind;
  }
  /** userId con el que el guest de esta invitacion entra al meeting: el propio id (UUID; ver join-ticket.ts). */
  get guestUserId(): string {
    return this.state.id;
  }
}
