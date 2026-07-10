import type { Message, MessageSnapshot } from '../../domain/conversations/message.js';

/**
 * Operaciones que mutan un mensaje SIN cargar el aggregate completo
 * (edit / delete / mark-batch-read). Cada operacion valida tenant y ownership
 * antes de aplicar. Cierra el N+1 legacy (mark_messages_read por lote).
 */
export interface MessageRepository {
  findById(tenantId: string, messageId: string): Promise<Message | null>;

  update(tenantId: string, message: Message): Promise<void>;

  /**
   * Marca receipts de lectura para todos los mensajes de una conversacion
   * hasta `lastReadMessageId`. Idempotente: si el receipt ya existe con
   * `ReadAtUtc`, no se sobrescribe.
   */
  markBatchRead(input: {
    tenantId: string;
    conversationId: string;
    userId: string;
    lastReadMessageId: string;
    now: Date;
  }): Promise<{ markedCount: number }>;

  /**
   * Registra que el usuario recibio el mensaje (Delivered). Insert-or-ignore
   * a nivel BD para evitar N+1 upserts como el legacy.
   */
  recordDelivered(input: {
    tenantId: string;
    conversationId: string;
    messageIds: readonly string[];
    userId: string;
    now: Date;
  }): Promise<void>;

  listByIds(tenantId: string, ids: readonly string[]): Promise<MessageSnapshot[]>;
}
