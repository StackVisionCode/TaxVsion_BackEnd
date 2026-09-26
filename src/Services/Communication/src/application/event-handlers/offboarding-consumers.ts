import { randomUUID } from 'node:crypto';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { Meeting } from '../../domain/meetings/meeting.js';
import { MeetingStatus } from '../../domain/meetings/meeting-enums.js';
import {
  MeetingEventTypes,
  type MeetingCancelledEvent,
  type MeetingEndedEvent,
  type MeetingHostTransferredEvent,
} from '../../contracts/events/meeting-events.js';
import {
  MeetingSocketEvents,
  type MeetingCancelledDto,
  type MeetingListChangedDto,
} from '../../contracts/socket/meeting-socket-events.js';

const CANCEL_REASON = 'The host was removed from the tenant.';

/**
 * Offboarding — retiro TERMINAL de un empleado (auth.user.offboarded.v1). A diferencia de
 * `deactivated` (baja reversible), aca hay que re-duenar su trabajo activo. Lo unico que
 * Communication no cubre por otra via es el HOST de reuniones: el dominio solo deja actuar al
 * host, asi que una reunion agendada/viva de un host retirado no la puede iniciar ni cancelar
 * nadie. El ruteo de chat por preparador se auto-cura via los eventos `customer.preparer_*`
 * que Customer republica al retirar al preparador (los consume customer-consumers.ts).
 */
export function bindOffboardingConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    userPermissions: UserPermissionsProjectionRepository;
    userDirectory: UserDirectoryRepository;
    customerPortalAccounts: CustomerPortalAccountRepository;
    meetings: MeetingRepository;
    publisher: IntegrationEventPublisher;
    emitter: RealtimeEmitter;
  },
): void {
  register('auth.user.offboarded.v1', async (env) => {
    const userId = getString(env.payload, 'userId') ?? getString(env.payload, 'UserId');
    if (!userId) return;

    // 1. Baseline: offboard incluye la baja — se inactivan las mismas 3 proyecciones que
    //    `deactivated`. Idempotente; offboard es terminal en Auth (no llega un reactivated luego).
    await deps.userPermissions.markInactive(userId, new Date());
    await deps.userDirectory.markInactive(userId);
    await deps.customerPortalAccounts.markInactiveByUserId(userId);

    // 2. ¿Hay un sucesor elegible (empleado activo del mismo tenant)? Se resuelve una sola vez.
    const successorId =
      getString(env.payload, 'successorUserId') ?? getString(env.payload, 'SuccessorUserId');
    const successor = await resolveEligibleSuccessor(deps.userDirectory, env.tenantId, successorId, userId);

    // 3. Reasignar (al sucesor) o cancelar/terminar (sin sucesor) cada reunion activa del que se va.
    const actorUserId =
      getString(env.payload, 'offboardedByUserId') ?? getString(env.payload, 'OffboardedByUserId') ?? userId;
    const hosted = await deps.meetings.listActiveHostedBy(env.tenantId, userId);
    for (const meeting of hosted) {
      if (successor) {
        await reassignHost(meeting, successor, env, deps);
      } else {
        await cancelOrEnd(meeting, actorUserId, env, deps);
      }
    }
  });
}

/** El sucesor solo sirve si existe, esta activo y es staff interno del mismo tenant (no un portal). */
async function resolveEligibleSuccessor(
  userDirectory: UserDirectoryRepository,
  tenantId: string,
  successorId: string | undefined,
  leaverUserId: string,
): Promise<{ userId: string; displayName: string } | null> {
  if (!successorId || successorId === leaverUserId) return null;
  const entry = await userDirectory.findByUserId(successorId);
  if (!entry || entry.tenantId !== tenantId || !entry.isActive) return null;
  if (entry.actorType !== 'TenantEmployee' && entry.actorType !== 'TenantAdmin') return null;
  return { userId: entry.userId, displayName: entry.displayName };
}

async function reassignHost(
  meeting: Meeting,
  successor: { userId: string; displayName: string },
  env: IncomingEnvelope,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher },
): Promise<void> {
  const previousHostUserId = meeting.hostUserId;
  const result = meeting.reassignHostBySystem({
    newHostUserId: successor.userId,
    newHostDisplayName: successor.displayName,
  });
  if (!result.isSuccess) return;
  await deps.meetings.save(meeting);

  const now = new Date();
  const event: MeetingHostTransferredEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.HostTransferred,
    tenantId: env.tenantId,
    correlationId: env.correlationId ?? '',
    occurredOnUtc: now.toISOString(),
    meetingId: meeting.id,
    previousHostUserId,
    newHostUserId: successor.userId,
    transferredAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);
}

async function cancelOrEnd(
  meeting: Meeting,
  actorUserId: string,
  env: IncomingEnvelope,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher; emitter: RealtimeEmitter },
): Promise<void> {
  const wasLive = meeting.status === MeetingStatus.Live;
  const result = wasLive ? meeting.endBySystem() : meeting.cancelBySystem();
  if (!result.isSuccess) return;
  await deps.meetings.save(meeting);

  const snapshot = meeting.toSnapshot();
  const now = new Date();
  const participantUserIds = snapshot.participants.map((p) => p.userId);

  if (wasLive) {
    const event: MeetingEndedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.Ended,
      tenantId: env.tenantId,
      correlationId: env.correlationId ?? '',
      occurredOnUtc: now.toISOString(),
      meetingId: meeting.id,
      hostUserId: snapshot.hostUserId,
      endedAtUtc: now.toISOString(),
      durationSeconds: snapshot.durationSeconds ?? 0,
      participantCount: snapshot.participants.length,
      recordingFileId: snapshot.recordingFileId,
    };
    await deps.publisher.enqueue(event);
    // Realtime: los participantes ven la reunion pasar de "upcoming" a "past" sin recargar.
    const dto: MeetingListChangedDto = { meetingId: meeting.id };
    for (const uid of new Set(participantUserIds)) {
      deps.emitter.emitToUser({
        tenantId: env.tenantId,
        userId: uid,
        event: MeetingSocketEvents.Ended,
        envelope: {
          eventId: randomUUID(),
          correlationId: env.correlationId ?? '',
          emittedAtUtc: now.toISOString(),
          payload: dto,
        },
      });
    }
    return;
  }

  // Scheduled -> Cancelled. Se recuperan los emails de invitados-por-link (aun no unidos) para
  // que Notification pueda avisarles, igual que cancel-meeting.ts.
  const invitations = await deps.meetings.listInvitationsByMeeting(env.tenantId, meeting.id);
  const invitedEmails = invitations
    .map((inv) => inv.toSnapshot())
    .filter((s) => s.usedAtUtc === null && s.revokedAtUtc === null && s.inviteeEmail !== null)
    .map((s) => s.inviteeEmail!);

  const event: MeetingCancelledEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.Cancelled,
    tenantId: env.tenantId,
    correlationId: env.correlationId ?? '',
    occurredOnUtc: now.toISOString(),
    meetingId: meeting.id,
    cancelledByUserId: actorUserId,
    cancelledAtUtc: now.toISOString(),
    participantUserIds,
    invitedEmails,
    reason: CANCEL_REASON,
  };
  await deps.publisher.enqueue(event);

  const dto: MeetingCancelledDto = {
    meetingId: meeting.id,
    cancelledByUserId: actorUserId,
    reason: CANCEL_REASON,
    cancelledAtUtc: now.toISOString(),
  };
  deps.emitter.emitToMeeting({
    tenantId: env.tenantId,
    meetingId: meeting.id,
    event: MeetingSocketEvents.Cancelled,
    envelope: {
      eventId: randomUUID(),
      correlationId: env.correlationId ?? '',
      emittedAtUtc: now.toISOString(),
      payload: dto,
    },
  });
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}
