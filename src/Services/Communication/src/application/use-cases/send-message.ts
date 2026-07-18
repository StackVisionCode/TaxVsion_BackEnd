import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import type { AttachmentTrackingRepository } from '../ports/attachment-tracking-repository.js';
import { ChatEventTypes, type MessageSentEvent } from '../../contracts/events/chat-events.js';
import type { MessageDto } from '../../contracts/socket/chat-socket-events.js';
import { messageSnapshotToDto } from './chat-mappers.js';

/**
 * Comando: enviar un mensaje a una conversacion existente. Body (Text) o
 * AttachmentFileId (referencia CloudStorage), no ambos.
 *
 * Reglas:
 *   1. Idempotencia por (tenantId, senderId, clientKey).
 *   2. sender debe ser participante activo — validado por el aggregate.
 *   3. Publica MessageSentEvent SIN el contenido (solo IDs).
 *
 * Preserva compatibilidad con el legacy FE: incluye senderDisplayName (cierra
 * CRIT-17), evita N+1 upsert de receipts (Fase 1 solo persiste el mensaje, los
 * receipts se materializan por lote al leer).
 */

export interface SendMessageCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly conversationId: string;
  readonly senderUserId: string;
  readonly body?: string | undefined;
  readonly attachmentFileId?: string | undefined;
  readonly replyToMessageId?: string | undefined;
}

export interface SendMessageResult {
  readonly message: MessageDto;
}

export interface SendMessageDeps {
  readonly conversations: ConversationRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly settings: TenantSettingsProvider;
  readonly attachmentTracking: AttachmentTrackingRepository;
}

export async function sendMessage(
  command: SendMessageCommand,
  deps: SendMessageDeps,
): Promise<Result<SendMessageResult>> {
  const settings = await deps.settings.get(command.tenantId);
  if (!settings.chatEnabled) {
    return Result.fail(makeError('Chat.Disabled', 'Chat is disabled for this tenant.'));
  }
  if (command.attachmentFileId !== undefined && command.body !== undefined) {
    return Result.fail(makeError('Chat.Message.MixedKind', 'Send either body or attachmentFileId, not both.'));
  }
  if (command.attachmentFileId === undefined && command.body === undefined) {
    return Result.fail(makeError('Chat.Message.Empty', 'Either body or attachmentFileId is required.'));
  }
  if (command.attachmentFileId !== undefined && !settings.screenshotsEnabled) {
    return Result.fail(
      makeError('Chat.Attachments.Disabled', 'Attachments are disabled for this tenant.'),
    );
  }

  const reservation = await deps.idempotency.tryReserve<SendMessageResult>({
    tenantId: command.tenantId,
    userId: command.senderUserId,
    scope: 'chat.message.send',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') {
    return Result.ok(reservation.payload);
  }

  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId, 0);
  if (!conversation) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.senderUserId,
      scope: 'chat.message.send',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }

  const messageResult =
    command.attachmentFileId !== undefined
      ? conversation.sendAttachment({
          senderId: command.senderUserId,
          attachmentFileId: command.attachmentFileId,
          replyToMessageId: command.replyToMessageId ?? null,
        })
      : conversation.sendText({
          senderId: command.senderUserId,
          body: command.body!,
          replyToMessageId: command.replyToMessageId ?? null,
        });
  if (!messageResult.isSuccess) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.senderUserId,
      scope: 'chat.message.send',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(messageResult.error);
  }

  await deps.conversations.save(conversation);

  const messageSnapshot = messageResult.value.toSnapshot();

  if (messageSnapshot.attachmentFileId !== null) {
    await deps.attachmentTracking.register({
      fileId: messageSnapshot.attachmentFileId,
      messageId: messageSnapshot.id,
      conversationId: command.conversationId,
      tenantId: command.tenantId,
    });
  }

  const recipients = conversation
    .getParticipantSnapshots()
    .filter((p) => !p.isRemoved && p.userId !== command.senderUserId)
    .map((p) => p.userId);

  const sentEvent: MessageSentEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.MessageSent,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: messageSnapshot.createdAtUtc.toISOString(),
    conversationId: command.conversationId,
    messageId: messageSnapshot.id,
    senderId: command.senderUserId,
    kind: messageSnapshot.kind,
    hasAttachment: messageSnapshot.attachmentFileId !== null,
    recipientUserIds: recipients,
    sentAtUtc: messageSnapshot.createdAtUtc.toISOString(),
  };
  await deps.publisher.enqueue(sentEvent);

  const dto: MessageDto = messageSnapshotToDto(messageSnapshot);
  const result: SendMessageResult = { message: dto };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.senderUserId,
    scope: 'chat.message.send',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
