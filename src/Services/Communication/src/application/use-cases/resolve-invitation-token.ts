import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingInvitation } from '../../domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import { signJoinTicket } from '../../infrastructure/jwks/join-ticket.js';

export interface ResolveInvitationTokenCommand {
  readonly token: string;
  readonly displayName?: string;
}

export interface ResolveInvitationTokenResult {
  readonly shortLivedJoinTicket: string;
  readonly meetingId: string;
  readonly shortCode: string;
  readonly tenantId: string;
  readonly invitationId: string;
}

export interface ResolveInvitationTokenDeps {
  readonly meetings: MeetingRepository;
  /** F6 (D-47): la oficina puede no admitir externos; sin resolver (tests viejos) se asume permitido. */
  readonly effectiveSettings?: Pick<EffectiveSettingsResolver, 'resolve'> | undefined;
}

const GENERIC_NOT_FOUND = makeError(
  'Meeting.Invitation.NotFound',
  'Invitation not found or no longer valid.',
);

/**
 * HTTP publico (join-by-token) — NO marca la invitation como usada; eso pasa
 * recien cuando el guest efectivamente entra al meeting via socket
 * (join-meeting.ts, ver MeetingInvitation.consumeForGuestJoin). Esto permite
 * recargar la pagina de join sin quemar el token de un solo intento.
 *
 * Anti-enumeracion: cualquier motivo de invalidez (no existe, revocada, ya
 * usada, expirada, hash no matchea) colapsa al MISMO codigo de error — el
 * route la mapea siempre a 404, sin filtrar por que fallo especificamente.
 * `Meeting.GuestsNotAllowed` es distinto a proposito: el token es valido, la
 * politica de la oficina es la que no admite externos (403, sin enumerar nada).
 */
export async function resolveInvitationToken(
  command: ResolveInvitationTokenCommand,
  deps: ResolveInvitationTokenDeps,
): Promise<Result<ResolveInvitationTokenResult>> {
  const hash = MeetingInvitation.hash(command.token);
  const invitation = await deps.meetings.findInvitationByHash(hash);
  if (!invitation) return Result.fail(GENERIC_NOT_FOUND);

  const now = new Date();
  const validation = invitation.validateForUse({ plainToken: command.token, now });
  const consumed = !validation.isSuccess && validation.error.code === 'Meeting.Invitation.AlreadyUsed';
  if (!validation.isSuccess && !consumed) return Result.fail(GENERIC_NOT_FOUND);

  const snap = invitation.toSnapshot();
  const meeting = await deps.meetings.findById(snap.tenantId, snap.meetingId);
  if (!meeting) return Result.fail(GENERIC_NOT_FOUND);
  // F6 (D-48): token ya consumido solo vale como re-join mientras su guest siga dentro o esperando.
  if (consumed) {
    const status = meeting.toSnapshot().participants.find((p) => p.userId === invitation.guestUserId)?.status ?? null;
    if (!invitation.allowsGuestRejoin(status) || MeetingInvitation.hash(command.token) !== snap.tokenHash) {
      return Result.fail(GENERIC_NOT_FOUND);
    }
  }

  // F6: el ticket es la sesion del guest, asi que dura lo que puede durar la reunion desde ahora (tope
  // de duracion + gracia de desconexion), nunca mas alla de la invitacion. El TTL de config es el minimo.
  let ttlSeconds = 0;
  if (deps.effectiveSettings) {
    const effective = await deps.effectiveSettings.resolve(snap.tenantId);
    if (!effective.meeting.guestsAllowed) {
      return Result.fail(makeError('Meeting.GuestsNotAllowed', 'This office does not allow external guests.'));
    }
    const untilInvitationExpires = Math.floor((snap.expiresAtUtc.getTime() - now.getTime()) / 1000);
    ttlSeconds = Math.max(0, Math.min(effective.meeting.maxDurationMinutes * 60 + effective.meeting.disconnectGraceSeconds, untilInvitationExpires));
  }

  const displayName = (
    command.displayName?.trim() ||
    snap.inviteeName ||
    deriveNameFromEmail(snap.inviteeEmail) ||
    'Invitado'
  )
    .trim()
    .slice(0, 120);

  const shortLivedJoinTicket = await signJoinTicket({
    invitationId: snap.id,
    meetingId: snap.meetingId,
    tenantId: snap.tenantId,
    displayName,
    ttlSeconds,
  });

  return Result.ok({
    shortLivedJoinTicket,
    meetingId: snap.meetingId,
    shortCode: meeting.toSnapshot().shortCode,
    tenantId: snap.tenantId,
    invitationId: snap.id,
  });
}

function deriveNameFromEmail(email: string | null): string | null {
  if (!email) return null;
  const [local] = email.split('@');
  return local && local.length > 0 ? local : null;
}
