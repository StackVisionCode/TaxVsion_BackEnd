/**
 * Foco del chat por sesión de socket: si la app está visible y qué conversación tiene abierta.
 * Solo decide push (no es presencia pública). El lease lo renueva el cliente al reemitir, no el latido.
 */
export interface ConversationFocusStore {
  set(input: {
    tenantId: string;
    userId: string;
    sessionId: string;
    visible: boolean;
    conversationId: string | null;
    leaseSeconds: number;
  }): Promise<void>;
  clear(input: { tenantId: string; userId: string; sessionId: string }): Promise<void>;
  /** De `userIds`: quién tiene alguna sesión visible y quién tiene `conversationId` abierta y visible. */
  snapshot(
    tenantId: string,
    userIds: readonly string[],
    conversationId: string,
  ): Promise<{ active: readonly string[]; viewing: readonly string[] }>;
}
