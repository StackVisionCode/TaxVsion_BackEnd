/**
 * Actividad de varias conversaciones vista por UN usuario: cuántos mensajes ajenos no leyó y cuál fue el último.
 * Lo usa la bandeja de Support para marcar qué ticket trae el mensaje nuevo.
 */
export interface ConversationLastMessage {
  readonly senderId: string;
  readonly kind: string;
  readonly body: string | null;
  readonly isDeleted: boolean;
  readonly createdAtUtc: Date;
}

export interface ConversationActivity {
  readonly unreadCount: number;
  readonly lastMessage: ConversationLastMessage | null;
}

export interface ConversationActivityReader {
  readForUser(input: {
    tenantId: string;
    userId: string;
    conversationIds: readonly string[];
  }): Promise<ReadonlyMap<string, ConversationActivity>>;
}
