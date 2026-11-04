import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { Meeting } from '../../domain/meetings/meeting.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { TenantHostResolver } from '../ports/tenant-host-resolver.js';
import {
  MeetingSocketEvents,
  type MeetingParticipantDeniedDto,
  type MeetingStateDto,
} from '../../contracts/socket/meeting-socket-events.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';
import { signGuestLinkToken } from '../../infrastructure/jwks/guest-link-token.js';
import { config } from '../../infrastructure/config.js';

export interface MeetingGuestAccessDeps extends MeetingEventLogDeps {
  readonly meetings: MeetingRepository;
  /** Se cablea tarde en el arranque (como en el resto de las rutas); sin el, no hay aviso en vivo. */
  readonly emitter?: RealtimeEmitter | undefined;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  readonly tenantHostResolver: TenantHostResolver;
}

export interface MeetingGuestAccessCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly actorUserId: string;
}

/** Lo que el host ve del acceso de externos: si se admiten y el enlace vigente (null = apagado). */
export interface MeetingGuestAccessDto {
  readonly guestsAllowed: boolean;
  /** La oficina (o la plataforma) no admite externos: la reunion no puede abrirse a ellos. */
  readonly officeAllowsGuests: boolean;
  readonly guestLinkUrl: string | null;
}

/** Host o cohost: ve el acceso de externos y el enlace (el token se deriva, nunca se guarda). */
export async function getMeetingGuestAccess(
  cmd: MeetingGuestAccessCommand,
  deps: MeetingGuestAccessDeps,
): Promise<Result<MeetingGuestAccessDto>> {
  const loaded = await loadForHost(cmd, deps);
  if (!loaded.isSuccess) return Result.fail(loaded.error);
  return Result.ok(await toDto(loaded.value, deps));
}

/** D-69: abre o cierra la reunion a externos; al cerrarla se rechaza a los que esperaban. */
export async function setMeetingGuestAccess(
  cmd: MeetingGuestAccessCommand & { readonly allowed: boolean },
  deps: MeetingGuestAccessDeps,
): Promise<Result<MeetingGuestAccessDto>> {
  const loaded = await loadForHost(cmd, deps);
  if (!loaded.isSuccess) return Result.fail(loaded.error);
  const meeting = loaded.value;
  if (cmd.allowed && !(await officeAllowsGuests(cmd.tenantId, deps))) return Result.fail(officeForbids());
  const result = meeting.setGuestAccess({ hostUserId: cmd.actorUserId, allowed: cmd.allowed });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const now = new Date();
  for (const userId of result.value.deniedUserIds) notifyDenied(cmd, userId, now, deps);
  emitAccessState(meeting, cmd.correlationId, now, deps);
  await recordMeetingEvent(deps, {
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    type: MeetingEventType.GuestAccessChanged,
    actorUserId: cmd.actorUserId,
    correlationId: cmd.correlationId,
    at: now,
    payload: { guestsAllowed: cmd.allowed, deniedWaitingGuests: result.value.deniedUserIds.length },
  });
  return Result.ok(await toDto(meeting, deps));
}

/** D-68: enciende (o rota, con `rotate`) el enlace de la reunion para externos. */
export async function enableMeetingGuestLink(
  cmd: MeetingGuestAccessCommand & { readonly rotate: boolean },
  deps: MeetingGuestAccessDeps,
): Promise<Result<MeetingGuestAccessDto>> {
  const loaded = await loadForHost(cmd, deps);
  if (!loaded.isSuccess) return Result.fail(loaded.error);
  const meeting = loaded.value;
  if (!(await officeAllowsGuests(cmd.tenantId, deps))) return Result.fail(officeForbids());
  const result = meeting.enableGuestLink({ hostUserId: cmd.actorUserId, rotate: cmd.rotate });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);
  const now = new Date();
  emitAccessState(meeting, cmd.correlationId, now, deps);
  await recordMeetingEvent(deps, {
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    type: MeetingEventType.GuestLinkChanged,
    actorUserId: cmd.actorUserId,
    correlationId: cmd.correlationId,
    at: now,
    payload: { link: cmd.rotate ? 'rotated' : 'enabled', version: result.value.version },
  });
  return Result.ok(await toDto(meeting, deps));
}

/** D-68: apaga el enlace; quien ya entro sigue, nadie nuevo puede pedir entrar con el. */
export async function disableMeetingGuestLink(
  cmd: MeetingGuestAccessCommand,
  deps: MeetingGuestAccessDeps,
): Promise<Result<MeetingGuestAccessDto>> {
  const loaded = await loadForHost(cmd, deps);
  if (!loaded.isSuccess) return Result.fail(loaded.error);
  const meeting = loaded.value;
  const result = meeting.disableGuestLink({ hostUserId: cmd.actorUserId });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);
  const now = new Date();
  emitAccessState(meeting, cmd.correlationId, now, deps);
  await recordMeetingEvent(deps, {
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    type: MeetingEventType.GuestLinkChanged,
    actorUserId: cmd.actorUserId,
    correlationId: cmd.correlationId,
    at: now,
    payload: { link: 'disabled' },
  });
  return Result.ok(await toDto(meeting, deps));
}

async function loadForHost(cmd: MeetingGuestAccessCommand, deps: MeetingGuestAccessDeps): Promise<Result<Meeting>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  if (cmd.actorUserId !== meeting.hostUserId && !meeting.isCohost(cmd.actorUserId)) {
    return Result.fail(makeError('Meeting.HostOnly', 'Only the host or a cohost can manage external guests.'));
  }
  return Result.ok(meeting);
}

async function officeAllowsGuests(tenantId: string, deps: MeetingGuestAccessDeps): Promise<boolean> {
  return (await deps.effectiveSettings.resolve(tenantId)).meeting.guestsAllowed;
}

function officeForbids() {
  return makeError('Meeting.GuestsNotAllowed', 'This office does not allow external guests.');
}

async function toDto(meeting: Meeting, deps: MeetingGuestAccessDeps): Promise<MeetingGuestAccessDto> {
  const m = meeting.toSnapshot();
  const office = await officeAllowsGuests(m.tenantId, deps);
  if (!m.guestLinkEnabled || !meeting.acceptsGuestLink(m.guestLinkVersion)) {
    return { guestsAllowed: m.guestsAllowed, officeAllowsGuests: office, guestLinkUrl: null };
  }
  // Mismo destino que las invitaciones externas: la pagina publica del CRM en el subdominio de la oficina.
  const host = await deps.tenantHostResolver.resolveHost(m.tenantId).catch(() => null);
  const base = host ? `https://${host}` : config.meetingInvitations.frontendBaseUrl.replace(/\/+$/, '');
  const token = signGuestLinkToken(m.id, m.guestLinkVersion);
  return { guestsAllowed: m.guestsAllowed, officeAllowsGuests: office, guestLinkUrl: `${base}/meet/${encodeURIComponent(token)}` };
}

function emitAccessState(meeting: Meeting, correlationId: string, now: Date, deps: MeetingGuestAccessDeps): void {
  const m = meeting.toSnapshot();
  const dto: MeetingStateDto = {
    meetingId: m.id,
    status: m.status,
    isLocked: m.isLocked,
    hostUserId: m.hostUserId,
    sequence: 0,
    guestsAllowed: m.guestsAllowed,
    guestLinkEnabled: m.guestLinkEnabled,
  };
  deps.emitter?.emitToMeeting({
    tenantId: m.tenantId,
    meetingId: m.id,
    event: MeetingSocketEvents.StateChanged,
    envelope: { eventId: randomUUID(), correlationId, emittedAtUtc: now.toISOString(), payload: dto },
  });
}

function notifyDenied(cmd: MeetingGuestAccessCommand, userId: string, now: Date, deps: MeetingGuestAccessDeps): void {
  const dto: MeetingParticipantDeniedDto = {
    meetingId: cmd.meetingId,
    participantUserId: userId,
    deniedByUserId: cmd.actorUserId,
    deniedAtUtc: now.toISOString(),
  };
  const envelope = { eventId: randomUUID(), correlationId: cmd.correlationId, emittedAtUtc: now.toISOString(), payload: dto };
  deps.emitter?.emitToUser({ tenantId: cmd.tenantId, userId, event: MeetingSocketEvents.ParticipantDenied, envelope });
  deps.emitter?.emitToMeeting({ tenantId: cmd.tenantId, meetingId: cmd.meetingId, event: MeetingSocketEvents.ParticipantDenied, envelope });
}
