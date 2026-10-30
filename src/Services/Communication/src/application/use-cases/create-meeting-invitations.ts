import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingInvitation, type MeetingInviteeKind } from '../../domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { TenantHostResolver } from '../ports/tenant-host-resolver.js';
import { MeetingEventTypes, type MeetingInvitationCreatedEvent } from '../../contracts/events/meeting-events.js';
import { MeetingSocketEvents, type MeetingInvitedDto } from '../../contracts/socket/meeting-socket-events.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';
import { resolveUserDisplayName } from './resolve-user-display-name.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import { buildMeetingJoinUrl } from './build-meeting-join-url.js';
import { config } from '../../infrastructure/config.js';
import type { EffectiveCommunicationSettings } from '../../domain/settings/effective-communication-settings.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';

/**
 * D-48: la invitación vive hasta el fin previsto de la reunión más una gracia, y nunca menos que el
 * mínimo global (hoy 7 días, lo que antes era una constante). Sin fecha programada cuenta desde ahora.
 */
export function invitationTtlSeconds(input: {
  now: Date;
  scheduledForUtc: Date | null;
  effective: Pick<EffectiveCommunicationSettings, 'invitations' | 'meeting'>;
}): number {
  const minExpiry = input.now.getTime() + input.effective.invitations.minTtlHours * 3_600_000;
  const start = input.scheduledForUtc ?? input.now;
  const meetingEndWithGrace =
    start.getTime() +
    input.effective.meeting.maxDurationMinutes * 60_000 +
    input.effective.invitations.expiryGraceHours * 3_600_000;
  return Math.ceil((Math.max(minExpiry, meetingEndWithGrace) - input.now.getTime()) / 1000);
}

export interface CreateMeetingInvitationInput {
  readonly kind: MeetingInviteeKind;
  readonly userId?: string;
  /**
   * Solo Customer: su cuenta de portal tiene un UserId de Auth distinto al customerId. Se
   * resuelve aca (customerId -> userId de portal activo) para poblar InviteeUserId, de modo que
   * la lista de meetings del cliente (query por userId) muestre el meeting y el aviso realtime
   * (room `t:{tenant}:u:{userId}`) le llegue. Sin cuenta de portal activa queda solo email.
   */
  readonly customerId?: string;
  readonly email?: string;
  readonly name?: string;
}

export interface CreateMeetingInvitationsCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly actorUserId: string;
  readonly invitees: readonly CreateMeetingInvitationInput[];
}

export interface CreateMeetingInvitationSummary {
  readonly id: string;
  /** Solo los primeros 8 chars — el token completo nunca se re-expone tras crearse. */
  readonly tokenPreview: string;
  readonly expiresAt: string;
  readonly inviteeEmail: string | null;
  readonly joinUrl: string;
}

export interface CreateMeetingInvitationsResult {
  readonly invitations: readonly CreateMeetingInvitationSummary[];
}

export interface CreateMeetingInvitationsDeps extends MeetingEventLogDeps {
  readonly meetings: MeetingRepository;
  /** F6: nombres legibles del host y de quien invita para el email y el toast. */
  readonly userDirectory?: Pick<UserDirectoryRepository, 'findByUserId'> | undefined;
  readonly publisher: IntegrationEventPublisher;
  readonly customerPortalAccounts: CustomerPortalAccountRepository;
  readonly tenantHostResolver: TenantHostResolver;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  /** Opcional (best-effort): si esta cableado, empuja `meeting.invited` al room del invitado. */
  readonly emitter?: RealtimeEmitter;
}

/**
 * Host/Cohost invitan N personas a un meeting. Cada invitee genera su propio
 * MeetingInvitation (token CSPRNG + hash), sin importar si es Employee/
 * Customer (tienen cuenta Auth — el token solo destraba isLocked) o External
 * (sin cuenta — entra 100% via join-by-token + shortLivedJoinTicket, Fase
 * Backend 5). Se valida el batch completo ANTES de persistir nada, para no
 * dejar invitaciones a medias si una entrada del batch es invalida.
 */
export async function createMeetingInvitations(
  command: CreateMeetingInvitationsCommand,
  deps: CreateMeetingInvitationsDeps,
): Promise<Result<CreateMeetingInvitationsResult>> {
  if (command.invitees.length === 0) {
    return Result.fail(makeError('Meeting.Invitation.EmptyBatch', 'At least one invitee is required.'));
  }

  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const canAct = command.actorUserId === meeting.hostUserId || meeting.isCohost(command.actorUserId);
  if (!canAct) return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can invite.'));
  const meetingSnapshot = meeting.toSnapshot();
  // F6 (T-62): a una reunion terminada, cancelada o vencida no se invita a nadie.
  if (meetingSnapshot.status !== 'Scheduled' && meetingSnapshot.status !== 'Live') {
    return Result.fail(makeError('Meeting.NotJoinable', `Meeting is ${meetingSnapshot.status}.`));
  }

  const now = new Date();
  const effective = await deps.effectiveSettings.resolve(command.tenantId);
  const ttlSeconds = invitationTtlSeconds({
    now,
    scheduledForUtc: meeting.toSnapshot().scheduledForUtc,
    effective,
  });
  const issued: Array<{ invitation: MeetingInvitation; plainToken: string }> = [];
  for (const invitee of command.invitees) {
    // Empleado: el userId de Auth ya viene del picker. Cliente: se resuelve su userId de portal
    // activo a partir del customerId (el picker de customers no expone userId). External: sin userId.
    let inviteeUserId = invitee.userId ?? null;
    if (!inviteeUserId && invitee.kind === 'Customer' && invitee.customerId) {
      const account = await deps.customerPortalAccounts.findActiveByCustomerId(invitee.customerId);
      inviteeUserId = account?.userId ?? null;
    }
    const issueResult = MeetingInvitation.issue({
      meetingId: command.meetingId,
      tenantId: command.tenantId,
      inviteeKind: invitee.kind,
      inviteeEmail: invitee.email ?? null,
      inviteeUserId,
      inviteeName: invitee.name ?? null,
      ttlSeconds,
      now,
    });
    if (!issueResult.isSuccess) return Result.fail(issueResult.error);
    issued.push(issueResult.value);
  }

  // El link va al SUBDOMINIO del tenant (bug prod: salía app./client.taxoffix.io). Se resuelve el
  // host UNA vez por batch (cacheado). Si falla, buildMeetingJoinUrl cae al base fijo.
  const tenantHost = await deps.tenantHostResolver.resolveHost(command.tenantId);
  const timeZone = (await deps.tenantHostResolver.resolveTimeZone?.(command.tenantId)) ?? null;
  const nameOf = (userId: string) => meetingSnapshot.participants.find((p) => p.userId === userId)?.displayName;
  const hostDisplayName = await resolveUserDisplayName(deps.userDirectory, meetingSnapshot.hostUserId, nameOf(meetingSnapshot.hostUserId));
  const invitedByDisplayName =
    command.actorUserId === meetingSnapshot.hostUserId
      ? hostDisplayName
      : await resolveUserDisplayName(deps.userDirectory, command.actorUserId, nameOf(command.actorUserId));
  const meetingStatus: 'Scheduled' | 'Live' = meetingSnapshot.status === 'Live' ? 'Live' : 'Scheduled';
  const durationMinutes = meetingSnapshot.scheduledDurationMinutes ?? meetingSnapshot.maxDurationMinutes ?? effective.meeting.defaultDurationMinutes;

  const summaries: CreateMeetingInvitationSummary[] = [];
  for (const { invitation, plainToken } of issued) {
    await deps.meetings.saveInvitation(invitation);
    const snap = invitation.toSnapshot();
    const joinUrl = buildMeetingJoinUrl({
      host: tenantHost,
      inviteeKind: snap.inviteeKind,
      meetingId: snap.meetingId,
      token: plainToken,
      fallbackBaseUrl: config.meetingInvitations.frontendBaseUrl,
      portalPathPrefix: config.meetingInvitations.portalPathPrefix,
    });

    const event: MeetingInvitationCreatedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.InvitationCreated,
      tenantId: command.tenantId,
      correlationId: command.correlationId,
      occurredOnUtc: snap.createdAtUtc.toISOString(),
      invitationId: snap.id,
      meetingId: snap.meetingId,
      inviteeKind: snap.inviteeKind,
      inviteeUserId: snap.inviteeUserId,
      inviteeEmail: snap.inviteeEmail,
      inviteeName: snap.inviteeName,
      tokenHash: snap.tokenHash,
      expiresAtUtc: snap.expiresAtUtc.toISOString(),
      joinUrl,
      meetingTitle: meetingSnapshot.title,
      meetingStatus,
      shortCode: meetingSnapshot.shortCode,
      scheduledForUtc: meetingSnapshot.scheduledForUtc ? meetingSnapshot.scheduledForUtc.toISOString() : null,
      durationMinutes,
      hostUserId: meetingSnapshot.hostUserId,
      hostDisplayName,
      timeZone,
      invitedByUserId: command.actorUserId,
      invitedByDisplayName,
    };
    await deps.publisher.enqueue(event);
    await recordMeetingEvent(deps, {
      tenantId: command.tenantId,
      meetingId: snap.meetingId,
      type: MeetingEventType.InvitationCreated,
      actorUserId: command.actorUserId,
      targetUserId: snap.inviteeUserId,
      payload: { invitationId: snap.id, inviteeKind: snap.inviteeKind, hasEmail: snap.inviteeEmail !== null },
      correlationId: command.correlationId,
      at: snap.createdAtUtc,
    });

    // Realtime: el invitado con cuenta (Employee/Customer con portal) ve el meeting aparecer en su
    // lista sin recargar. External (sin userId) solo recibe el email — no tiene UX de meetings.
    if (deps.emitter && snap.inviteeUserId) {
      const dto: MeetingInvitedDto = {
        meetingId: snap.meetingId,
        title: meetingSnapshot.title,
        status: meetingStatus,
        invitedByUserId: command.actorUserId,
        invitedByDisplayName,
      };
      deps.emitter.emitToUser({
        tenantId: command.tenantId,
        userId: snap.inviteeUserId,
        event: MeetingSocketEvents.Invited,
        envelope: {
          eventId: randomUUID(),
          correlationId: command.correlationId,
          emittedAtUtc: now.toISOString(),
          payload: dto,
        },
      });
    }

    summaries.push({
      id: snap.id,
      tokenPreview: `${plainToken.slice(0, 8)}...`,
      expiresAt: snap.expiresAtUtc.toISOString(),
      inviteeEmail: snap.inviteeEmail,
      joinUrl,
    });
  }

  return Result.ok({ invitations: summaries });
}
