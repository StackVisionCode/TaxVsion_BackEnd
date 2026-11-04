import { randomUUID } from 'node:crypto';
import { Conversation } from '../../domain/conversations/conversation.js';
import type { ConversationParticipantSnapshot } from '../../domain/conversations/conversation-participant.js';
import { ConversationKind } from '../../domain/conversations/conversation-kind.js';
import { computeDirectUniquenessKey } from '../../domain/conversations/uniqueness-key.js';
import type { Message } from '../../domain/conversations/message.js';
import { SystemMessageKind } from '../../domain/conversations/message-kind.js';
import { isTerminalSupport } from '../../domain/support/support-enums.js';
import type { SupportTicketSnapshot } from '../../domain/support/support-ticket.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { ConversationAuditEntry, ConversationAuditRepository } from '../ports/conversation-audit-repository.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import { ChatEventTypes, type ConversationStartedEvent } from '../../contracts/events/chat-events.js';
import {
  ChatSocketEvents,
  type ConversationCreatedDto,
  type ConversationParticipantRemovedDto,
  type ConversationUpdatedDto,
} from '../../contracts/socket/chat-socket-events.js';
import type { SocketEnvelope } from '../../contracts/socket/socket-envelope.js';
import { messageSnapshotToDto } from './chat-mappers.js';
import { CUSTOMERS_VIEW_ALL } from './customer-visibility.js';
import { failRecording, type RecordingLifecycleDeps } from './recording-lifecycle.js';
import { listUserConversations } from './user-conversations.js';
import { toDepartedParticipantItem } from './list-conversations.js';
import { logger } from '../../infrastructure/logger/logger.js';

/**
 * Retiro (offboarding) de un empleado — lo que Communication hace además del handover de sus reuniones como
 * host. Todo por sistema, idempotente (cada paso solo actúa sobre lo que sigue apuntando al retirado) y sin
 * tocar la procedencia: los mensajes, la auditoría y los consentimientos quedan como estaban.
 */
export interface StaffRef {
  readonly userId: string;
  readonly displayName: string;
  readonly actorType: string;
}

export interface OffboardingContext {
  readonly tenantId: string;
  readonly leaverUserId: string;
  /** Nombre del retirado para los mensajes de sistema (el del directorio; si no, el del participante). */
  readonly leaverDisplayName: string | null;
  /** Sucesor ya validado (staff activo de la oficina) o null. */
  readonly successor: StaffRef | null;
  /** Quién lo retiró (Auth exige TenantAdmin/PlatformAdmin): primer candidato a "admin de la oficina". */
  readonly offboardedByUserId: string | null;
  readonly correlationId: string;
}

/** Motivo de una grabación cerrada por el sistema porque quien la capturaba en su navegador se retiró. */
export const RECORDING_FAILURE_REQUESTER_OFFBOARDED = 'RequesterOffboarded';

function envelope<T>(correlationId: string, payload: T): SocketEnvelope<T> {
  return { eventId: randomUUID(), correlationId, emittedAtUtc: new Date().toISOString(), payload };
}

// ------------------------------------------------------------------
// Admin de la oficina (sin sucesor)
// ------------------------------------------------------------------

/**
 * "Admin de la oficina" = TenantAdmin activo de este tenant que ve a todos los clientes (`customers.view_all`,
 * el mismo criterio que deja chatear con cualquier cliente). Primero quien hizo el retiro; si no califica, el
 * primero estable (por id) de la proyección de permisos. null si la oficina no tiene ninguno.
 */
export async function resolveOfficeAdmin(
  deps: {
    userDirectory: Pick<UserDirectoryRepository, 'findByUserId'>;
    userPermissions: Pick<UserPermissionsProjectionRepository, 'findByUserId' | 'findActiveByTenantAndPermission'>;
  },
  input: { tenantId: string; preferredUserId: string | null; excludeUserId: string },
): Promise<StaffRef | null> {
  const asAdmin = async (userId: string): Promise<StaffRef | null> => {
    if (userId === input.excludeUserId) return null;
    const entry = await deps.userDirectory.findByUserId(userId);
    if (!entry || entry.tenantId !== input.tenantId || !entry.isActive || entry.actorType !== 'TenantAdmin') return null;
    return { userId: entry.userId, displayName: entry.displayName, actorType: entry.actorType };
  };

  if (input.preferredUserId) {
    const preferred = await asAdmin(input.preferredUserId);
    const permissions = preferred ? await deps.userPermissions.findByUserId(preferred.userId) : null;
    if (preferred && permissions?.isActive && permissions.permissions.includes(CUSTOMERS_VIEW_ALL)) return preferred;
  }
  const candidates = (await deps.userPermissions.findActiveByTenantAndPermission(input.tenantId, CUSTOMERS_VIEW_ALL))
    .filter((c) => c.actorType === 'TenantAdmin' && c.userId !== input.excludeUserId)
    .map((c) => c.userId)
    .sort();
  for (const userId of candidates) {
    const admin = await asAdmin(userId);
    if (admin) return admin;
  }
  return null;
}

// ------------------------------------------------------------------
// #4 — Reuniones de otros
// ------------------------------------------------------------------

/**
 * Sale de las reuniones agendadas de otros hosts (incluidas las que acaba de ceder como host) y se le revocan
 * las invitaciones abiertas: no vuelve a recibir recordatorios ni cancelaciones con título, hora y enlace.
 */
export async function removeFromOthersMeetings(
  ctx: OffboardingContext,
  deps: { meetings: MeetingRepository },
): Promise<{ meetingsLeft: number; invitationsRevoked: number }> {
  const now = new Date();
  let meetingsLeft = 0;
  for (const meeting of await deps.meetings.listScheduledWithParticipant(ctx.tenantId, ctx.leaverUserId)) {
    const result = meeting.removeParticipantBySystem({ userId: ctx.leaverUserId, now });
    if (!result.isSuccess || !result.value.changed) continue;
    await deps.meetings.save(meeting);
    meetingsLeft++;
  }
  let invitationsRevoked = 0;
  for (const invitation of await deps.meetings.listOpenInvitationsForInvitee(ctx.tenantId, ctx.leaverUserId)) {
    if (!invitation.revokeBySystem(now)) continue;
    await deps.meetings.saveInvitation(invitation);
    invitationsRevoked++;
  }
  return { meetingsLeft, invitationsRevoked };
}

// ------------------------------------------------------------------
// D-4 — Chats Direct (clientes y staff)
// ------------------------------------------------------------------

export interface DirectChatsDeps {
  readonly conversations: ConversationRepository;
  readonly conversationAudit?: ConversationAuditRepository | undefined;
  readonly emitter?: RealtimeEmitter | undefined;
  readonly publisher?: IntegrationEventPublisher | undefined;
  readonly customerPortalAccounts: Pick<CustomerPortalAccountRepository, 'findActiveByUserId'>;
  readonly settings?: Pick<TenantSettingsProvider, 'get'> | undefined;
  readonly userDirectory: Pick<UserDirectoryRepository, 'findByUserId'>;
  readonly userPermissions: Pick<UserPermissionsProjectionRepository, 'findByUserId' | 'findActiveByTenantAndPermission'>;
}

/**
 * Sus chats Direct: sale de todos (removido + mensaje de sistema). Con un cliente, además, el chat queda cerrado
 * (solo lectura; la oficina con permiso lo sigue leyendo) y, si era su preparador principal, el cliente sigue
 * con el sucesor (marcado primary) o, sin sucesor, con el admin de la oficina.
 */
export async function handOverDirectChats(
  ctx: OffboardingContext,
  deps: DirectChatsDeps,
): Promise<{ clientChatsClosed: number; staffChatsLeft: number; clientChatsOpened: number }> {
  const chats = await listUserConversations(deps.conversations, {
    tenantId: ctx.tenantId,
    userId: ctx.leaverUserId,
    kinds: [ConversationKind.Direct],
    includeArchived: true,
  });
  const report = { clientChatsClosed: 0, staffChatsLeft: 0, clientChatsOpened: 0 };
  let fallbackAdmin: StaffRef | null | undefined;
  for (const chat of chats) {
    const conversation = await deps.conversations.findById(ctx.tenantId, chat.id, 0);
    if (!conversation) continue;
    const leaverName = nameIn(conversation, ctx);
    const now = new Date();
    const outcome = conversation.retireParticipantBySystem(ctx.leaverUserId, now);
    if (!outcome) continue;
    const client = outcome.counterpart?.actorType === 'CustomerPortal' ? outcome.counterpart : null;
    if (!client) {
      await commitSystemChange(conversation, ctx, deps, {
        text: `${leaverName} is no longer with the office`,
        audit: [{ action: 'MemberRemoved', actorUserId: null, targetUserId: ctx.leaverUserId, metadata: { reason: 'Offboarded' } }],
        now,
      });
      report.staffChatsLeft++;
      continue;
    }

    // Primero el chat que sigue (sucesor u oficina) y después el cierre del viejo: si algo falla a mitad, el
    // reintento vuelve a encontrar al retirado en el chat viejo y retoma ambos pasos.
    if (outcome.wasPrimaryPreparer) {
      if (fallbackAdmin === undefined && !ctx.successor) {
        fallbackAdmin = await resolveOfficeAdmin(deps, {
          tenantId: ctx.tenantId,
          preferredUserId: ctx.offboardedByUserId,
          excludeUserId: ctx.leaverUserId,
        });
      }
      const next = ctx.successor ?? fallbackAdmin ?? null;
      const opened = next
        ? await continueClientChatWith(ctx, deps, {
            client,
            staff: next,
            asPrimary: ctx.successor !== null,
            text: ctx.successor
              ? `Your preparer changed: ${next.displayName} is now your preparer and will continue here. Your previous conversation with ${leaverName} is closed.`
              : `${leaverName} is no longer with the office. You can keep messaging the office here.`,
          })
        : false;
      if (opened) report.clientChatsOpened++;
      else logger.warn({ tenantId: ctx.tenantId, conversationId: conversation.id }, 'offboarding: client left without a new office chat');
    }

    conversation.archive(now);
    await commitSystemChange(conversation, ctx, deps, {
      text: outcome.wasPrimaryPreparer
        ? `Your preparer changed: ${leaverName} is no longer with the office. This conversation is now read-only.`
        : `${leaverName} is no longer with the office. This conversation is now read-only.`,
      audit: [
        {
          action: 'ClosedByOffboarding',
          actorUserId: null,
          targetUserId: ctx.leaverUserId,
          metadata: { wasPrimaryPreparer: outcome.wasPrimaryPreparer, successorUserId: ctx.successor?.userId ?? null },
        },
      ],
      archived: true,
      now,
    });
    report.clientChatsClosed++;
  }
  return report;
}

/** Abre (o reutiliza) el Direct cliente↔staff, lo marca primary si toca y deja el aviso. */
async function continueClientChatWith(
  ctx: OffboardingContext,
  deps: DirectChatsDeps,
  input: { client: ConversationParticipantSnapshot; staff: StaffRef; asPrimary: boolean; text: string },
): Promise<boolean> {
  const settings = deps.settings ? await deps.settings.get(ctx.tenantId) : null;
  if (settings && !settings.chatEnabled) return false;
  // Sin cuenta de portal activa no hay a quién abrirle el chat.
  const portal = await deps.customerPortalAccounts.findActiveByUserId(input.client.userId);
  if (!portal || portal.tenantId !== ctx.tenantId) return false;

  const now = new Date();
  const key = computeDirectUniquenessKey(input.staff.userId, input.client.userId);
  let conversation = await deps.conversations.findByUniquenessKey(ctx.tenantId, key);
  const created = !conversation;
  if (!conversation) {
    const started = Conversation.startDirect({
      tenantId: ctx.tenantId,
      initiator: input.staff,
      recipient: { userId: input.client.userId, displayName: input.client.displayName, actorType: input.client.actorType },
      now,
    });
    if (!started.isSuccess) return false;
    conversation = started.value;
  }
  // Un chat cerrado no se reabre: el cliente sigue sin interlocutor (lo registra el log).
  if (conversation.isArchived) return false;
  const markedPrimary = input.asPrimary && conversation.markPrimaryPreparerBySystem(input.staff.userId, now);
  // Reintento: el chat ya existía y ya era el primary → el aviso ya se dejó.
  if (!created && input.asPrimary && !markedPrimary) return true;

  await commitSystemChange(conversation, ctx, deps, {
    text: input.text,
    audit: input.asPrimary
      ? [{ action: 'PrimaryPreparerAssigned', actorUserId: null, targetUserId: input.staff.userId, metadata: { previousUserId: ctx.leaverUserId } }]
      : [],
    now,
    joinUserIds: [input.staff.userId, input.client.userId],
  });
  if (created) await announceConversationStarted(conversation, ctx, deps, [input.staff.userId, input.client.userId], now);
  return true;
}

async function announceConversationStarted(
  conversation: Conversation,
  ctx: OffboardingContext,
  deps: Pick<DirectChatsDeps, 'publisher' | 'emitter'>,
  participantUserIds: readonly string[],
  now: Date,
): Promise<void> {
  const createdByUserId = conversation.toSnapshot().createdByUserId;
  if (deps.publisher) {
    const event: ConversationStartedEvent = {
      eventId: randomUUID(),
      eventType: ChatEventTypes.ConversationStarted,
      tenantId: ctx.tenantId,
      correlationId: ctx.correlationId,
      occurredOnUtc: now.toISOString(),
      conversationId: conversation.id,
      kind: 'Direct',
      createdByUserId,
      participantUserIds: [...participantUserIds],
    };
    await deps.publisher.enqueue(event);
  }
  const dto: ConversationCreatedDto = {
    id: conversation.id,
    kind: 'Direct',
    title: null,
    createdByUserId,
    createdAtUtc: now.toISOString(),
  };
  for (const userId of participantUserIds) {
    deps.emitter?.emitToUser({ tenantId: ctx.tenantId, userId, event: ChatSocketEvents.ConversationCreated, envelope: envelope(ctx.correlationId, dto) });
  }
}

// ------------------------------------------------------------------
// D-5 — Support
// ------------------------------------------------------------------

export interface SupportHandoverDeps {
  readonly supportTickets: SupportTicketRepository;
  readonly conversations: ConversationRepository;
  readonly conversationAudit?: ConversationAuditRepository | undefined;
  readonly emitter?: RealtimeEmitter | undefined;
  readonly userDirectory: Pick<UserDirectoryRepository, 'findByUserId'>;
  readonly userPermissions: Pick<UserPermissionsProjectionRepository, 'findByUserId' | 'findActiveByTenantAndPermission'>;
}

const SUPPORT_PAGE = 100;

/**
 * Tickets abiertos por el retirado → al sucesor (dueño + participante del chat) o, sin sucesor, al admin de la
 * oficina. Tickets que atendía como agente de plataforma → vuelven a la cola (Open, sin agente). Auditado.
 */
export async function handOverSupportTickets(
  ctx: OffboardingContext,
  deps: SupportHandoverDeps,
): Promise<{ transferred: number; released: number }> {
  const report = { transferred: 0, released: 0 };
  const opened = await collectTickets((skip) =>
    deps.supportTickets.listForCustomer({ tenantId: ctx.tenantId, openedByUserId: ctx.leaverUserId, take: SUPPORT_PAGE, skip }),
  );
  if (opened.length > 0) {
    const target =
      ctx.successor ??
      (await resolveOfficeAdmin(deps, { tenantId: ctx.tenantId, preferredUserId: ctx.offboardedByUserId, excludeUserId: ctx.leaverUserId }));
    if (!target) {
      logger.warn({ tenantId: ctx.tenantId, tickets: opened.length }, 'offboarding: open support tickets without successor or office admin');
    } else {
      for (const ticketId of opened) if (await transferTicket(ticketId, target, ctx, deps)) report.transferred++;
    }
  }
  const assigned = await collectTickets((skip) =>
    deps.supportTickets.listForAgentTenant({ agentTenantId: ctx.tenantId, assignedAgentId: ctx.leaverUserId, take: SUPPORT_PAGE, skip }),
  );
  for (const ticketId of assigned) if (await releaseTicket(ticketId, ctx, deps)) report.released++;
  return report;
}

/** Ids de los tickets no terminales de un listado paginado (se juntan antes de mutar). */
async function collectTickets(page: (skip: number) => Promise<readonly SupportTicketSnapshot[]>): Promise<string[]> {
  const ids: string[] = [];
  for (let skip = 0; ; skip += SUPPORT_PAGE) {
    const rows = await page(skip);
    for (const row of rows) if (!isTerminalSupport(row.status) && !ids.includes(row.id)) ids.push(row.id);
    if (rows.length < SUPPORT_PAGE) return ids;
  }
}

async function transferTicket(ticketId: string, target: StaffRef, ctx: OffboardingContext, deps: SupportHandoverDeps): Promise<boolean> {
  const ticket = await deps.supportTickets.findById(ticketId);
  if (!ticket || ticket.tenantId !== ctx.tenantId || ticket.openedByUserId !== ctx.leaverUserId) return false;
  const now = new Date();
  const moved = ticket.transferOwnershipBySystem({ newOpenerUserId: target.userId, now });
  if (!moved.isSuccess) return false;
  await deps.supportTickets.save(ticket);

  const conversation = await deps.conversations.findById(ticket.tenantId, ticket.conversationId, 0);
  if (!conversation) return true;
  const leaverName = nameIn(conversation, ctx);
  conversation.replaceSupportRequesterBySystem({ previousUserId: ctx.leaverUserId, next: target, now });
  await commitSystemChange(conversation, ctx, deps, {
    text: `${leaverName} is no longer with the office. This ticket now belongs to ${target.displayName}.`,
    audit: [
      {
        action: 'SupportTicketTransferred',
        actorUserId: null,
        targetUserId: target.userId,
        metadata: { ticketId: ticket.id, previousOpenerUserId: moved.value.previousOpenerUserId, to: ctx.successor ? 'Successor' : 'OfficeAdmin' },
      },
    ],
    now,
    joinUserIds: [target.userId],
  });
  return true;
}

async function releaseTicket(ticketId: string, ctx: OffboardingContext, deps: SupportHandoverDeps): Promise<boolean> {
  const ticket = await deps.supportTickets.findById(ticketId);
  if (!ticket || ticket.agentTenantId !== ctx.tenantId || ticket.assignedAgentId !== ctx.leaverUserId) return false;
  const now = new Date();
  const released = ticket.releaseAgentBySystem({ now });
  if (!released.isSuccess) return false;
  await deps.supportTickets.save(ticket);

  // El chat vive en la oficina que abrió el ticket; el agente nunca fue participante.
  const conversation = await deps.conversations.findById(ticket.tenantId, ticket.conversationId, 0);
  if (!conversation) return true;
  await commitSystemChange(conversation, { ...ctx, tenantId: ticket.tenantId }, deps, {
    text: 'The support agent on this ticket is no longer available. It is back in the queue and another agent will pick it up.',
    systemKind: SystemMessageKind.Notice,
    audit: [{ action: 'SupportTicketReleased', actorUserId: null, targetUserId: ctx.leaverUserId, metadata: { ticketId: ticket.id } }],
    now,
  });
  return true;
}

// ------------------------------------------------------------------
// Grabaciones con captura en su navegador
// ------------------------------------------------------------------

/** Sus grabaciones Client aún capturando quedan Failed por sistema: su navegador ya no subirá el archivo. */
export async function failRequestedClientRecordings(
  ctx: OffboardingContext,
  deps: RecordingLifecycleDeps & { recordingSessions: RecordingSessionRepository },
): Promise<number> {
  let failed = 0;
  for (const session of await deps.recordingSessions.listCaptureActiveClientByRequester(ctx.tenantId, ctx.leaverUserId)) {
    await failRecording(session, RECORDING_FAILURE_REQUESTER_OFFBOARDED, deps, ctx.correlationId);
    failed++;
  }
  return failed;
}

// ------------------------------------------------------------------
// Efecto común: mensaje de sistema + guardar + auditar + avisar
// ------------------------------------------------------------------

type AuditDraft = Omit<ConversationAuditEntry, 'tenantId' | 'conversationId' | 'occurredAtUtc'>;

async function commitSystemChange(
  conversation: Conversation,
  ctx: OffboardingContext,
  deps: { conversations: ConversationRepository; conversationAudit?: ConversationAuditRepository | undefined; emitter?: RealtimeEmitter | undefined },
  change: {
    text: string;
    audit: readonly AuditDraft[];
    now: Date;
    archived?: boolean;
    joinUserIds?: readonly string[];
    /** Sin dato: Membership (casi todo el retiro es alguien que sale o entra). */
    systemKind?: SystemMessageKind;
  },
): Promise<void> {
  const system = conversation.appendSystemMessage({
    body: change.text,
    systemKind: change.systemKind ?? SystemMessageKind.Membership,
    now: change.now,
  });
  await deps.conversations.save(conversation);

  if (deps.conversationAudit && change.audit.length > 0) {
    // El cambio ya quedó hecho: si la bitácora falla no se revierte, se deja rastro en el log.
    await deps.conversationAudit
      .append(change.audit.map((a) => ({ ...a, tenantId: conversation.tenantId, conversationId: conversation.id, occurredAtUtc: change.now })))
      .catch((err: unknown) => logger.error({ err, conversationId: conversation.id }, 'offboarding audit: no se pudo registrar'));
  }
  if (deps.emitter) emitSystemChange(deps.emitter, conversation, ctx, system.isSuccess ? system.value : null, change);
}

function emitSystemChange(
  emitter: RealtimeEmitter,
  conversation: Conversation,
  ctx: OffboardingContext,
  message: Message | null,
  change: { now: Date; archived?: boolean; joinUserIds?: readonly string[] },
): void {
  const tenantId = conversation.tenantId;
  const leftNow = conversation.getParticipantSnapshots().some((p) => p.userId === ctx.leaverUserId && p.isRemoved);
  for (const userId of change.joinUserIds ?? []) emitter.addUserToConversation?.({ tenantId, userId, conversationId: conversation.id });
  if (message) {
    emitter.emitToConversation({
      tenantId,
      conversationId: conversation.id,
      event: ChatSocketEvents.MessageNew,
      envelope: envelope(ctx.correlationId, { ...messageSnapshotToDto(message.toSnapshot()), conversationKind: conversation.kind, meetingId: null }),
    });
  }
  const departed = leftNow ? conversation.departedParticipant() : null;
  if (change.archived || departed) {
    // Solo lectura en vivo: archivado (chat de cliente) o Direct de staff cuyo otro participante se fue.
    const dto: ConversationUpdatedDto = {
      conversationId: conversation.id,
      kind: conversation.kind,
      ...(change.archived ? { isArchived: true } : {}),
      isClosedForWriting: conversation.isClosedForWriting,
      closedReason: conversation.closedReason,
      ...(departed ? { departedParticipant: toDepartedParticipantItem(departed) } : {}),
    };
    emitter.emitToConversation({ tenantId, conversationId: conversation.id, event: ChatSocketEvents.ConversationUpdated, envelope: envelope(ctx.correlationId, dto) });
  }
  if (leftNow) {
    const dto: ConversationParticipantRemovedDto = {
      conversationId: conversation.id,
      removedByUserId: ctx.leaverUserId,
      removedParticipantUserId: ctx.leaverUserId,
      reason: 'Kicked',
      removedAtUtc: change.now.toISOString(),
    };
    emitter.emitToConversation({ tenantId, conversationId: conversation.id, event: ChatSocketEvents.ConversationParticipantRemoved, envelope: envelope(ctx.correlationId, dto) });
    emitter.removeUserFromConversation?.({ tenantId, userId: ctx.leaverUserId, conversationId: conversation.id });
  }
}

/** Nombre del retirado: el del directorio y, si no está, el que figura en la conversación. */
function nameIn(conversation: Conversation, ctx: OffboardingContext): string {
  return (
    ctx.leaverDisplayName ??
    conversation.getParticipantSnapshots().find((p) => p.userId === ctx.leaverUserId)?.displayName ??
    'A team member'
  );
}
