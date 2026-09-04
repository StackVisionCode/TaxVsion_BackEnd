import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { DeliveryReceiptDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Marca ENTREGA (delivered) de todos los mensajes de una conversación hasta
 * `upToMessageId` para el usuario que los RECIBIÓ. Es el estado intermedio entre
 * "enviado" (1 cotejo) y "leído" (2 azules): lo dispara el receptor al RECIBIR el
 * mensaje por socket SIN abrir la conversación. Espeja `mark-messages-read.ts` pero
 * NO toca la lectura — estar conectado no debe poner "leído".
 *
 * Idempotente (WHERE DeliveredAtUtc IS NULL). No mueve el puntero de lectura del
 * participante; solo registra receipts de entrega.
 */
export interface MarkMessagesDeliveredCommand {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly userUserId: string;
  readonly upToMessageId: string;
}

export interface MarkMessagesDeliveredResult {
  readonly receipt: DeliveryReceiptDto;
  readonly markedCount: number;
}

export interface MarkMessagesDeliveredDeps {
  readonly conversations: ConversationRepository;
  readonly messages: MessageRepository;
}

export async function markMessagesDelivered(
  command: MarkMessagesDeliveredCommand,
  deps: MarkMessagesDeliveredDeps,
): Promise<Result<MarkMessagesDeliveredResult>> {
  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId, 0);
  if (!conversation) {
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }
  if (!conversation.isParticipant(command.userUserId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }

  const now = new Date();
  const { markedCount } = await deps.messages.markBatchDelivered({
    tenantId: command.tenantId,
    conversationId: command.conversationId,
    userId: command.userUserId,
    upToMessageId: command.upToMessageId,
    now,
  });

  return Result.ok({
    receipt: {
      conversationId: command.conversationId,
      userId: command.userUserId,
      upToMessageId: command.upToMessageId,
      deliveredAtUtc: now.toISOString(),
    },
    markedCount,
  });
}
