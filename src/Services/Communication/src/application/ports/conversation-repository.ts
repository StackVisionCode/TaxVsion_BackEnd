import type { Conversation, ConversationSnapshot } from '../../domain/conversations/conversation.js';
import type { MessageSnapshot } from '../../domain/conversations/message.js';
import type { ConversationKind } from '../../domain/conversations/conversation-kind.js';

/**
 * Puerto principal para persistir el aggregate Conversation. Guarda la raiz +
 * participants + los pending messages en UNA transaccion (cierra CRIT-8 del
 * legacy: sin write parcial). El repo NUNCA re-lee historial completo — solo
 * los ultimos N mensajes cuando se re-hidrata.
 */
export interface ConversationRepository {
  save(conversation: Conversation): Promise<void>;

  findById(tenantId: string, id: string, recentMessagesLimit?: number): Promise<Conversation | null>;

  findByUniquenessKey(tenantId: string, uniquenessKey: string): Promise<Conversation | null>;

  listForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
    includeArchived?: boolean;
    /** F8a: solo estos tipos (Chat pide Direct,Group); vacío o ausente = todos. */
    kinds?: readonly ConversationKind[];
  }): Promise<ConversationSnapshot[]>;

  countForUser(tenantId: string, userId: string, includeArchived?: boolean, kinds?: readonly ConversationKind[]): Promise<number>;

  /** F8a: no leídos por tipo en conversaciones activas (badges separados de Chat y Support). */
  countUnreadByKind(tenantId: string, userId: string): Promise<Record<ConversationKind, number>>;

  /**
   * `beforeUtc` pagina hacia atras (scrollback, desc). `afterUtc` pagina hacia
   * adelante (asc) — usado para backfill al reconectar: el cliente manda el
   * `createdAtUtc` del ultimo mensaje que ya tiene y recibe todo lo posterior
   * en orden cronologico. Mutuamente excluyentes (ver getMessages).
   */
  listMessages(input: {
    tenantId: string;
    conversationId: string;
    beforeUtc?: Date;
    afterUtc?: Date;
    /** F8b (D-21): el cliente de un grupo no ve lo anterior a su ingreso. */
    notBeforeUtc?: Date | null;
    take: number;
  }): Promise<MessageSnapshot[]>;

  countUnreadForUser(input: {
    tenantId: string;
    conversationId: string;
    userId: string;
  }): Promise<number>;
}
