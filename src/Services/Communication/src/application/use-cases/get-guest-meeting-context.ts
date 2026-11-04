import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingInvitation } from '../../domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import { toMediaPolicyDto, type MediaPolicyDto } from './media-policy-query.js';
import { resolveUserDisplayName } from './resolve-user-display-name.js';
import { parseGuestLinkToken, verifyGuestLinkToken } from '../../infrastructure/jwks/guest-link-token.js';
import type { Meeting } from '../../domain/meetings/meeting.js';

export interface GuestMeetingContextDto {
  readonly meetingTitle: string;
  readonly hostDisplayName: string;
  readonly scheduledForUtc: string | null;
  readonly durationMinutes: number | null;
  readonly status: 'Scheduled' | 'Live' | 'Ended';
  readonly shortCode: string;
  readonly inviteeName: string | null;
  readonly guestsAllowed: boolean;
  readonly waitingRoom: boolean;
  /** D-68: entra por el enlace de la reunion (el invitado escribe su nombre). */
  readonly viaMeetingLink: boolean;
  /** D-68: la reunion esta bloqueada; por el enlace no se puede pedir entrar. */
  readonly isLocked: boolean;
  /** Politica de media de la oficina (sin preferencias de usuario): el guest no tiene JWT para pedirla. */
  readonly mediaPolicy: MediaPolicyDto;
}

export interface GetGuestMeetingContextDeps {
  readonly meetings: MeetingRepository;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  readonly userDirectory?: Pick<UserDirectoryRepository, 'findByUserId'> | undefined;
}

const GENERIC_NOT_FOUND = makeError('Meeting.Invitation.NotFound', 'Invitation not found or no longer valid.');

/**
 * F6 (T-63, D-47): lo que la pagina publica de guest del CRM muestra antes de pedir el nombre. No
 * consume nada ni emite ticket. Anti-enumeracion: cualquier token invalido (no existe, revocado,
 * vencido, consumido) colapsa al mismo 404; una reunion ya terminada con token valido SI se cuenta
 * (`status: 'Ended'`), igual que la politica de guests apagada (`guestsAllowed: false`).
 */
export async function getGuestMeetingContext(input: { token: string }, deps: GetGuestMeetingContextDeps): Promise<Result<GuestMeetingContextDto>> {
  const link = parseGuestLinkToken(input.token);
  if (link) return guestLinkContext(input.token, link.meetingId, deps);
  const invitation = await deps.meetings.findInvitationByHash(MeetingInvitation.hash(input.token));
  if (!invitation) return Result.fail(GENERIC_NOT_FOUND);
  const snap = invitation.toSnapshot();
  const now = new Date();
  const validation = invitation.validateForUse({ plainToken: input.token, now });
  // Cerrada por la reunion: el token es autentico, se informa que termino en vez de un 404 ciego.
  const closedByMeeting = !validation.isSuccess && validation.error.code === 'Meeting.Invitation.Closed';
  // Consumida pero con el guest aun dentro/esperando: es su recarga (D-48), no un segundo uso.
  const consumed = !validation.isSuccess && validation.error.code === 'Meeting.Invitation.AlreadyUsed';
  if (!validation.isSuccess && !closedByMeeting && !consumed) return Result.fail(GENERIC_NOT_FOUND);

  const meeting = await deps.meetings.findById(snap.tenantId, snap.meetingId);
  if (!meeting) return Result.fail(GENERIC_NOT_FOUND);
  const m = meeting.toSnapshot();
  if (consumed) {
    const status = m.participants.find((p) => p.userId === invitation.guestUserId)?.status ?? null;
    if (!invitation.allowsGuestRejoin(status) || MeetingInvitation.hash(input.token) !== snap.tokenHash) {
      return Result.fail(GENERIC_NOT_FOUND);
    }
  }
  const effective = await deps.effectiveSettings.resolve(snap.tenantId);
  const status: GuestMeetingContextDto['status'] = m.status === 'Scheduled' ? 'Scheduled' : m.status === 'Live' ? 'Live' : 'Ended';

  return Result.ok({
    meetingTitle: m.title,
    hostDisplayName: await resolveUserDisplayName(
      deps.userDirectory,
      m.hostUserId,
      m.participants.find((p) => p.userId === m.hostUserId)?.displayName,
    ),
    scheduledForUtc: m.scheduledForUtc ? m.scheduledForUtc.toISOString() : null,
    durationMinutes: m.scheduledDurationMinutes ?? m.maxDurationMinutes ?? effective.meeting.defaultDurationMinutes,
    status,
    shortCode: m.shortCode,
    inviteeName: snap.inviteeName,
    guestsAllowed: effective.meeting.guestsAllowed && m.guestsAllowed,
    // D-67: un externo siempre espera admision.
    waitingRoom: true,
    viaMeetingLink: false,
    isLocked: m.isLocked,
    mediaPolicy: toMediaPolicyDto(effective),
  });
}

/** D-68: contexto del enlace de la reunion; un enlace rotado, apagado o de otra version es un 404 ciego. */
async function guestLinkContext(token: string, meetingId: string, deps: GetGuestMeetingContextDeps): Promise<Result<GuestMeetingContextDto>> {
  const meeting = await deps.meetings.findByIdAnyTenant(meetingId);
  if (!meeting || !validLink(meeting, token)) return Result.fail(GENERIC_NOT_FOUND);
  const m = meeting.toSnapshot();
  const effective = await deps.effectiveSettings.resolve(m.tenantId);
  return Result.ok({
    meetingTitle: m.title,
    hostDisplayName: await resolveUserDisplayName(
      deps.userDirectory,
      m.hostUserId,
      m.participants.find((p) => p.userId === m.hostUserId)?.displayName,
    ),
    scheduledForUtc: m.scheduledForUtc ? m.scheduledForUtc.toISOString() : null,
    durationMinutes: m.scheduledDurationMinutes ?? m.maxDurationMinutes ?? effective.meeting.defaultDurationMinutes,
    status: m.status === 'Scheduled' ? 'Scheduled' : 'Live',
    shortCode: m.shortCode,
    inviteeName: null,
    guestsAllowed: effective.meeting.guestsAllowed && m.guestsAllowed,
    waitingRoom: true,
    viaMeetingLink: true,
    isLocked: m.isLocked,
    mediaPolicy: toMediaPolicyDto(effective),
  });
}

function validLink(meeting: Meeting, token: string): boolean {
  const m = meeting.toSnapshot();
  return m.guestLinkEnabled && verifyGuestLinkToken(token, m.guestLinkVersion) && meeting.acceptsGuestLink(m.guestLinkVersion);
}
