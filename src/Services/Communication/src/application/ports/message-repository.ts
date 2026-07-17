import type { Message, MessageSnapshot } from '../../domain/conversations/message.js';
import type { MessageReaction, MessageReactionSnapshot } from '../../domain/conversations/message-reaction.js';

/**
 * Operaciones que mutan un mensaje SIN cargar el aggregate completo
 * (edit / delete / mark-batch-read). Cada operacion valida tenant y ownership
 * antes de aplicar. Cierra el N+1 legacy (mark_messages_read por lote).
 */
export interface MessageRepository {
  findById(tenantId: string, messageId: string): Promise<Message | null>;

  update(tenantId: string, message: Message): Promise<void>;

  /**
   * Fase Backend 9 — persiste un mensaje reenviado (mismo shape que un send
   * normal pero via message-repo directo, ya que el forward no rehidrata el
   * Conversation destino completo — solo aplica ensureActiveParticipant al
   * caller). Devuelve void; el use case propaga el snapshot ya en memoria.
   */
  insertForwarded(tenantId: string, message: Message): Promise<void>;

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

  /**
   * Fase Backend 9 — reactions. Insert-or-noop sobre el unique
   * (MessageId, UserId, Emoji) — un usuario que "reacciona" con el mismo
   * emoji dos veces no genera dos filas. Devuelve `wasNew: false` en ese
   * caso para que el use case no re-emita el socket event.
   */
  addReaction(reaction: MessageReaction): Promise<{ wasNew: boolean }>;

  /** Elimina la fila exacta (MessageId, UserId, Emoji). Idempotente. */
  removeReaction(input: {
    tenantId: string;
    messageId: string;
    userId: string;
    emoji: string;
  }): Promise<{ wasPresent: boolean }>;

  listReactionsByMessage(tenantId: string, messageId: string): Promise<MessageReactionSnapshot[]>;

  /**
   * Fase Backend 9 — full-text search por LIKE (SQL Server sin Full-Text
   * catalog en el entorno actual). Filtrado siempre por tenant + conversation
   * + isDeleted=false. Devuelve solo mensajes visibles al llamador; la
   * verificacion de participacion la hace el use case ANTES de llamar aca.
   * Case-insensitive (usa el collation del DB, tipicamente CI_AI).
   */
  searchByBody(input: {
    tenantId: string;
    conversationId: string;
    query: string;
    limit: number;
  }): Promise<MessageSnapshot[]>;
}
