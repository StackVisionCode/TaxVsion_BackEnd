import type { MessageRepository } from '../ports/message-repository.js';
import type { PresenceService } from '../ports/presence-service.js';
import type { DeliveryReceiptDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Marca ENTREGA (delivered) de un mensaje RECIÉN enviado para los destinatarios que tienen una
 * sesión viva (socket conectado). Es la otra mitad de "conectado = entregado (2 grises)": el
 * backlog lo cubre `mark-connected-delivered` al conectar; esto cubre el mensaje que llega EN VIVO
 * mientras el receptor está online — aunque no tenga la conversación abierta ni esté en la página
 * de chat (su cliente no siempre está montado para auto-marcar). NO toca lectura: si el receptor
 * tiene la conversación abierta, su cliente marcará leído (2 azules) y eso prevalece (avance monótono).
 *
 * Idempotente: `recordDelivered` es insert-or-ignore; re-enviar el mismo mensaje (replay) no duplica.
 * Devuelve un `DeliveryReceiptDto` por destinatario online, para que el handler los emita y el EMISOR
 * pinte los 2 cotejos grises.
 */
export interface MarkLiveDeliveredCommand {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly messageId: string;
  readonly recipientUserIds: readonly string[];
}

export interface MarkLiveDeliveredDeps {
  readonly presence: PresenceService;
  readonly messages: MessageRepository;
}

export async function markLiveDelivered(
  command: MarkLiveDeliveredCommand,
  deps: MarkLiveDeliveredDeps,
): Promise<DeliveryReceiptDto[]> {
  if (command.recipientUserIds.length === 0) return [];

  const online = await deps.presence.listOnline(command.tenantId, command.recipientUserIds);
  if (online.length === 0) return [];

  const now = new Date();
  const receipts: DeliveryReceiptDto[] = [];
  for (const userId of online) {
    await deps.messages.recordDelivered({
      tenantId: command.tenantId,
      conversationId: command.conversationId,
      messageIds: [command.messageId],
      userId,
      now,
    });
    receipts.push({
      conversationId: command.conversationId,
      userId,
      upToMessageId: command.messageId,
      deliveredAtUtc: now.toISOString(),
    });
  }
  return receipts;
}
