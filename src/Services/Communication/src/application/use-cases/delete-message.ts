import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import { isGroupAdminRole } from '../../domain/conversations/conversation-participant.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { ChatEventTypes, type MessageDeletedEvent } from '../../contracts/events/chat-events.js';
import type { MessageDeletedDto } from '../../contracts/socket/chat-socket-events.js';

export interface DeleteMessageCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly messageId: string;
  readonly actorUserId: string;
  readonly actorCanModerate: boolean;
  /**
   * Permiso global `chat.moderate`. Es lo único que deja borrar en un chat cerrado (archivado o con el otro
   * fuera): retirar contenido indebido de un historial. Ni el autor ni el host/cohost o el Owner/Admin del
   * grupo (moderación del momento) tocan un historial cerrado.
   */
  readonly actorHasModeratePermission?: boolean;
}

export interface DeleteMessageResult {
  readonly deleted: MessageDeletedDto;
}

export interface DeleteMessageDeps {
  readonly messages: MessageRepository;
  /** F8b (D-22): Owner/Admin borran mensajes ajenos dentro de su grupo; también decide si está cerrada. */
  readonly conversations: Pick<ConversationRepository, 'findById'>;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
}


export async function deleteMessage(
  command: DeleteMessageCommand,
  deps: DeleteMessageDeps,
): Promise<Result<DeleteMessageResult>> {
  const reservation = await deps.idempotency.tryReserve<DeleteMessageResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'chat.message.delete',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'chat.message.delete',
      clientKey: command.clientKey,
      token: reservation.token,
    });

  const message = await deps.messages.findById(command.tenantId, command.messageId);
  if (!message) {
    await release();
    return Result.fail(makeError('Chat.Message.NotFound', 'Message not found.'));
  }

  const conversation = await deps.conversations.findById(command.tenantId, message.toSnapshot().conversationId, 0);
  if (!conversation) {
    await release();
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }
  // Chat cerrado: solo `chat.moderate` borra (cumplimiento); el resto recibe el mismo error que al enviar.
  const open = conversation.ensureOpen();
  if (!open.isSuccess && !command.actorHasModeratePermission) {
    await release();
    return Result.fail(open.error);
  }

  const now = new Date();
  const isGroupModerator = conversation.kind === 'Group' && isGroupAdminRole(conversation.roleOf(command.actorUserId));
  // Abierta: la moderación de siempre. Cerrada: ya se comprobó el permiso global.
  const canModerate = open.isSuccess ? command.actorCanModerate || isGroupModerator : true;
  const deleteResult = message.softDelete(command.actorUserId, canModerate, now);
  if (!deleteResult.isSuccess) {
    await release();
    return Result.fail(deleteResult.error);
  }
  await deps.messages.update(command.tenantId, message);

  const snapshot = message.toSnapshot();
  const deletedAtUtc = (snapshot.deletedAtUtc ?? now).toISOString();

  const event: MessageDeletedEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.MessageDeleted,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: deletedAtUtc,
    conversationId: snapshot.conversationId,
    messageId: snapshot.id,
    deletedByUserId: command.actorUserId,
    deletedAtUtc,
  };
  await deps.publisher.enqueue(event);

  const result: DeleteMessageResult = {
    deleted: {
      messageId: snapshot.id,
      conversationId: snapshot.conversationId,
      deletedAtUtc,
    },
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'chat.message.delete',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
