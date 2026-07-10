import type { RealtimeEmitter } from '../../application/ports/realtime-emitter.js';
import type { SocketEnvelope } from '../../contracts/socket/socket-envelope.js';
import type { CommunicationIoServer } from './build-io.js';

/**
 * Implementacion de RealtimeEmitter usando el Socket.IO Server. Todos los
 * emits pasan por rooms tenant-scoped — cierre defensivo contra emit
 * cross-tenant por bug de nombrado de canal.
 */
export class SocketRealtimeEmitter implements RealtimeEmitter {
  constructor(private readonly io: CommunicationIoServer) {}

  emitToConversation<T>(input: {
    tenantId: string;
    conversationId: string;
    event: string;
    envelope: SocketEnvelope<T>;
  }): void {
    this.io.to(`t:${input.tenantId}:c:${input.conversationId}`).emit(input.event, input.envelope);
  }

  emitToUser<T>(input: {
    tenantId: string;
    userId: string;
    event: string;
    envelope: SocketEnvelope<T>;
  }): void {
    this.io.to(`t:${input.tenantId}:u:${input.userId}`).emit(input.event, input.envelope);
  }
}
