import { randomUUID } from 'node:crypto';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { ConversationAuditRepository } from '../ports/conversation-audit-repository.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import { removeUserFromGroups } from '../use-cases/group-administration.js';
import {
  failRequestedClientRecordings,
  handOverDirectChats,
  handOverSupportTickets,
  removeFromOthersMeetings,
  type OffboardingContext,
} from '../use-cases/offboarding-handover.js';
import type { RecordingLifecycleDeps } from '../use-cases/recording-lifecycle.js';
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
  COMMUNICATION_OFFBOARDING_SERVICE,
  COMMUNICATION_OFFBOARDING_STEP,
  OFFBOARDING_STEP_COMPLETED_TYPE,
  type UserOffboardingStepCompletedEvent,
} from '../../contracts/events/offboarding-events.js';
import {
  MeetingSocketEvents,
  type MeetingCancelledDto,
  type MeetingListChangedDto,
} from '../../contracts/socket/meeting-socket-events.js';

const CANCEL_REASON = 'The host was removed from the tenant.';

/**
 * Offboarding — retiro TERMINAL de un empleado (auth.user.offboarded.v1). A diferencia de
 * `deactivated` (baja reversible, D-7: solo bloquea el acceso), aca se re-dueña su trabajo activo:
 * reuniones que hospeda, reuniones e invitaciones de otros (#4), grupos, chats Direct con clientes
 * (D-4) y con staff, tickets de soporte (D-5) y grabaciones que capturaba en su navegador.
 * Idempotente y sin orden asumido: cada paso actua solo sobre lo que sigue apuntando al retirado,
 * asi que un reintento (o un paso que falló) retoma donde quedó. La procedencia no se toca.
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
    /** F8b (T-19): sale de sus grupos con sucesión (Owner → Admin más antiguo → staff más antiguo). */
    conversations?: ConversationRepository;
    conversationAudit?: ConversationAuditRepository;
    /** D-5: tickets que abrió (al sucesor o al admin) o atendía como agente (vuelven a la cola). */
    supportTickets?: SupportTicketRepository;
    /** D-4: el chat nuevo con el sucesor solo se abre si la oficina tiene el chat encendido. */
    settings?: TenantSettingsProvider;
    /** Grabaciones con captura en su navegador que siguen abiertas: se cierran por sistema. */
    recordingLifecycle?: RecordingLifecycleDeps;
  },
): void {
  register('auth.user.offboarded.v1', async (env) => {
    const userId = getString(env.payload, 'userId') ?? getString(env.payload, 'UserId');
    if (!userId) return;
    // El nombre se lee antes de nada: los mensajes de sistema lo muestran (la fila se conserva inactiva).
    const leaverEntry = await deps.userDirectory.findByUserId(userId);

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
        await cancelOrEnd(meeting, actorUserId, userId, env, deps);
      }
    }

    const ctx: OffboardingContext = {
      tenantId: env.tenantId,
      leaverUserId: userId,
      leaverDisplayName: leaverEntry && leaverEntry.tenantId === env.tenantId ? leaverEntry.displayName : null,
      successor,
      offboardedByUserId:
        getString(env.payload, 'offboardedByUserId') ?? getString(env.payload, 'OffboardedByUserId') ?? null,
      correlationId: env.correlationId ?? '',
    };

    // 4. #4: sale de las reuniones agendadas de otros (incluidas las que acaba de ceder) y de sus invitaciones.
    await removeFromOthersMeetings(ctx, { meetings: deps.meetings });

    if (deps.conversations) {
      // 5. Grupos (archivados incluidos): sale de todos; la sucesión la resuelve cada aggregate.
      await removeUserFromGroups(
        { tenantId: env.tenantId, userId, reason: 'Offboarded' },
        { conversations: deps.conversations, conversationAudit: deps.conversationAudit, emitter: deps.emitter },
      );
      // 6. D-4: chats Direct (clientes: cerrar y seguir con el sucesor o la oficina; staff: removido).
      await handOverDirectChats(ctx, {
        conversations: deps.conversations,
        conversationAudit: deps.conversationAudit,
        emitter: deps.emitter,
        publisher: deps.publisher,
        customerPortalAccounts: deps.customerPortalAccounts,
        settings: deps.settings,
        userDirectory: deps.userDirectory,
        userPermissions: deps.userPermissions,
      });
      // 7. D-5: tickets de soporte.
      if (deps.supportTickets) {
        await handOverSupportTickets(ctx, {
          supportTickets: deps.supportTickets,
          conversations: deps.conversations,
          conversationAudit: deps.conversationAudit,
          emitter: deps.emitter,
          userDirectory: deps.userDirectory,
          userPermissions: deps.userPermissions,
        });
      }
    }

    // 8. Grabaciones Client que capturaba: su navegador ya no subirá el archivo.
    if (deps.recordingLifecycle) await failRequestedClientRecordings(ctx, deps.recordingLifecycle);

    // 9. Acuse a Auth (avance del retiro): solo si todo lo anterior terminó; un reintento lo repite y Auth lo absorbe.
    const completedAt = new Date().toISOString();
    const ack: UserOffboardingStepCompletedEvent = {
      eventId: randomUUID(),
      eventType: OFFBOARDING_STEP_COMPLETED_TYPE,
      tenantId: env.tenantId,
      correlationId: env.correlationId ?? '',
      occurredOnUtc: completedAt,
      userId,
      service: COMMUNICATION_OFFBOARDING_SERVICE,
      step: COMMUNICATION_OFFBOARDING_STEP,
      completedAtUtc: completedAt,
    };
    await deps.publisher.enqueue(ack);
  });
}

/** El sucesor solo sirve si existe, esta activo y es staff interno del mismo tenant (no un portal). */
async function resolveEligibleSuccessor(
  userDirectory: UserDirectoryRepository,
  tenantId: string,
  successorId: string | undefined,
  leaverUserId: string,
): Promise<{ userId: string; displayName: string; actorType: string } | null> {
  if (!successorId || successorId === leaverUserId) return null;
  const entry = await userDirectory.findByUserId(successorId);
  if (!entry || entry.tenantId !== tenantId || !entry.isActive) return null;
  if (entry.actorType !== 'TenantEmployee' && entry.actorType !== 'TenantAdmin') return null;
  return { userId: entry.userId, displayName: entry.displayName, actorType: entry.actorType };
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
    reason: 'Offboarding',
  };
  await deps.publisher.enqueue(event);
}

async function cancelOrEnd(
  meeting: Meeting,
  actorUserId: string,
  leaverUserId: string,
  env: IncomingEnvelope,
  deps: { meetings: MeetingRepository; publisher: IntegrationEventPublisher; emitter: RealtimeEmitter },
): Promise<void> {
  const wasLive = meeting.status === MeetingStatus.Live;
  const result = wasLive ? meeting.endBySystem({ reason: 'Offboarding' }) : meeting.cancelBySystem();
  if (!result.isSuccess) return;
  await deps.meetings.save(meeting);

  const snapshot = meeting.toSnapshot();
  const now = new Date();
  const participantUserIds = snapshot.participants.map((p) => p.userId);
  // #4: el aviso de cancelación (título, hora y enlace) no le llega al retirado.
  const notifiedUserIds = meeting.notifiableUserIds().filter((id) => id !== leaverUserId);

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
      reason: 'Offboarding',
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
  const invitations = (await deps.meetings.listInvitationsByMeeting(env.tenantId, meeting.id))
    .map((inv) => inv.toSnapshot())
    .filter((s) => s.revokedAtUtc === null);
  // F6: externos sin cuenta por email; los de cuenta por in-app.
  const invitedEmails = invitations.filter((s) => s.inviteeUserId === null && s.inviteeEmail !== null).map((s) => s.inviteeEmail!);
  const inviteeUserIds = [...new Set(invitations.filter((s) => s.inviteeUserId !== null).map((s) => s.inviteeUserId!))];
  const meetingSnapshot = meeting.toSnapshot();

  const event: MeetingCancelledEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.Cancelled,
    tenantId: env.tenantId,
    correlationId: env.correlationId ?? '',
    occurredOnUtc: now.toISOString(),
    meetingId: meeting.id,
    cancelledByUserId: actorUserId,
    cancelledAtUtc: now.toISOString(),
    participantUserIds: notifiedUserIds,
    invitedEmails,
    reason: CANCEL_REASON,
    meetingTitle: meetingSnapshot.title,
    shortCode: meetingSnapshot.shortCode,
    scheduledForUtc: meetingSnapshot.scheduledForUtc ? meetingSnapshot.scheduledForUtc.toISOString() : null,
    durationMinutes: meetingSnapshot.scheduledDurationMinutes ?? meetingSnapshot.maxDurationMinutes,
    hostUserId: meetingSnapshot.hostUserId,
    hostDisplayName: meetingSnapshot.participants.find((p) => p.userId === meetingSnapshot.hostUserId)?.displayName ?? 'Host',
    timeZone: null,
    inviteeUserIds,
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
