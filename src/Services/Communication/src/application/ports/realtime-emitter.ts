import type { SocketEnvelope } from '../../contracts/socket/socket-envelope.js';

/**
 * Abstraccion sobre Socket.IO para poder invocar `emitir a conversation` desde
 * use cases sin depender de la clase Server. Facilita testing (mock in-memory)
 * y reemplazo del transport si algun dia deja de ser Socket.IO (poco probable).
 *
 * Rooms convencion (usar el metodo apropiado; NO pasar prefijos como
 * `call:` o `m:` dentro de `conversationId` — eso rompe el room name):
 *   - `t:{tenantId}:c:{conversationId}` — la conversacion de chat.
 *   - `t:{tenantId}:call:{callId}` — el room de una call 1:1.
 *   - `t:{tenantId}:m:{meetingId}` — el room de un meeting multi-party.
 *   - `t:{tenantId}:u:{userId}` — todos los sockets del usuario.
 *   - `t:{tenantId}` — todos los sockets del tenant (broadcasts globales).
 */
export interface RealtimeEmitter {
  emitToConversation<T>(input: {
    tenantId: string;
    conversationId: string;
    event: string;
    envelope: SocketEnvelope<T>;
  }): void;

  emitToCall<T>(input: {
    tenantId: string;
    callId: string;
    event: string;
    envelope: SocketEnvelope<T>;
  }): void;

  emitToMeeting<T>(input: {
    tenantId: string;
    meetingId: string;
    event: string;
    envelope: SocketEnvelope<T>;
  }): void;

  emitToUser<T>(input: { tenantId: string; userId: string; event: string; envelope: SocketEnvelope<T> }): void;

  emitToTenant<T>(input: { tenantId: string; event: string; envelope: SocketEnvelope<T> }): void;
}
