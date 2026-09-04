import type { MessageRepository } from '../ports/message-repository.js';
import type { DeliveryReceiptDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Marca ENTREGA (delivered) del backlog entrante al CONECTAR. Hace cumplir la regla
 * "conectado = entregado (2 grises)" para los mensajes que llegaron ANTES de que el socket
 * estuviera vivo: el camino en vivo (`markMessagesDelivered`) solo cubre lo que llega por
 * evento con la conversación cerrada, así que el backlog quedaba en "enviado" (1 cotejo)
 * hasta que el receptor ABRÍA la conversación (y saltaba directo a leído). NO toca lectura.
 *
 * Devuelve un `DeliveryReceiptDto` por conversación con marcas nuevas, para que el handler
 * los emita a cada sala y el EMISOR pinte los 2 cotejos grises sobre sus mensajes.
 */
export interface MarkConnectedDeliveredCommand {
  readonly tenantId: string;
  readonly userUserId: string;
  readonly conversationIds: readonly string[];
}

export interface MarkConnectedDeliveredDeps {
  readonly messages: MessageRepository;
}

export async function markConnectedDelivered(
  command: MarkConnectedDeliveredCommand,
  deps: MarkConnectedDeliveredDeps,
): Promise<DeliveryReceiptDto[]> {
  if (command.conversationIds.length === 0) return [];
  const now = new Date();
  const marked = await deps.messages.markPendingDeliveredForConversations({
    tenantId: command.tenantId,
    userId: command.userUserId,
    conversationIds: command.conversationIds,
    now,
  });
  return marked.map((m) => ({
    conversationId: m.conversationId,
    userId: command.userUserId,
    upToMessageId: m.upToMessageId,
    deliveredAtUtc: now.toISOString(),
  }));
}
