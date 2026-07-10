import type { SocketEnvelope } from '../../contracts/socket/socket-envelope.js';

/**
 * Abstraccion sobre Socket.IO para poder invocar `emitir a conversation` desde
 * use cases sin depender de la clase Server. Facilita testing (mock in-memory)
 * y reemplazo del transport si algun dia deja de ser Socket.IO (poco probable).
 *
 * Rooms convencion:
 *   - `t:{tenantId}:c:{conversationId}` — la conversacion.
 *   - `t:{tenantId}:u:{userId}` — todos los sockets del usuario.
 */
export interface RealtimeEmitter {
  emitToConversation<T>(input: {
    tenantId: string;
    conversationId: string;
    event: string;
    envelope: SocketEnvelope<T>;
  }): void;

  emitToUser<T>(input: { tenantId: string; userId: string; event: string; envelope: SocketEnvelope<T> }): void;
}
