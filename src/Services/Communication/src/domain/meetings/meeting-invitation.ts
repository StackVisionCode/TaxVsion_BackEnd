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

/**
 * Invitacion a un meeting. Genera un token opaco cripto (32 bytes hex = 64
 * chars) y guarda solo su SHA-256 hex (128 chars). El token en claro se
 * devuelve UNA sola vez al creador — cierre CRIT-6 legacy.
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
  readonly usedAtUtc: Date | null;
  readonly revokedAtUtc: Date | null;
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
      createdAtUtc: input.now,
    };
    return Result.ok({ invitation: new MeetingInvitation(snapshot), plainToken });
  }

  static hash(plainToken: string): string {
    return createHash('sha256').update(plainToken).digest('hex');
  }

  validateForUse(input: { plainToken: string; now: Date }): Result<void> {
    if (this.state.revokedAtUtc !== null) {
      return Result.fail(makeError('Meeting.Invitation.Revoked', 'Invitation was revoked.'));
    }
    if (this.state.usedAtUtc !== null) {
      return Result.fail(makeError('Meeting.Invitation.AlreadyUsed', 'Invitation was already used.'));
    }
    if (input.now.getTime() > this.state.expiresAtUtc.getTime()) {
      return Result.fail(makeError('Meeting.Invitation.Expired', 'Invitation expired.'));
    }
    if (MeetingInvitation.hash(input.plainToken) !== this.state.tokenHash) {
      return Result.fail(makeError('Meeting.Invitation.InvalidToken', 'Invalid invitation token.'));
    }
    return Result.okVoid();
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
    if (this.state.revokedAtUtc !== null) {
      return Result.fail(makeError('Meeting.Invitation.Revoked', 'Invitation was revoked.'));
    }
    if (this.state.usedAtUtc !== null) {
      return Result.fail(makeError('Meeting.Invitation.AlreadyUsed', 'Invitation was already used.'));
    }
    if (now.getTime() > this.state.expiresAtUtc.getTime()) {
      return Result.fail(makeError('Meeting.Invitation.Expired', 'Invitation expired.'));
    }
    this.state = { ...this.state, usedAtUtc: now };
    return Result.okVoid();
  }

  markUsed(now: Date): void {
    this.state = { ...this.state, usedAtUtc: now };
  }

  revoke(now: Date): void {
    this.state = { ...this.state, revokedAtUtc: now };
  }

  toSnapshot(): MeetingInvitationSnapshot {
    return this.state;
  }
}
