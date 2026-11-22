/**
 * Actividad de varias conversaciones vista por UN usuario: cuántos mensajes ajenos no leyó y cuál fue el último.
 * Lo usan la bandeja de Support y la lista de conversaciones (esta con `historyFloor`).
 */
export interface ConversationLastMessage {
  readonly senderId: string;
  readonly kind: string;
  readonly body: string | null;
  readonly isDeleted: boolean;
  readonly createdAtUtc: Date;
  /** Solo con `historyFloor`. */
  readonly senderDisplayName?: string;
  readonly audioDurationMs?: number | null;
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
    /** Piso de ingreso en grupos (JoinedAtUtc) para no leídos y último mensaje; solo participantes activos. */
    historyFloor?: boolean;
  }): Promise<ReadonlyMap<string, ConversationActivity>>;
}
