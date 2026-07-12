import type { RealtimeEmitter } from '../../application/ports/realtime-emitter.js';
import type { SocketEnvelope } from '../../contracts/socket/socket-envelope.js';
import type { CommunicationIoServer } from './build-io.js';

/**
 * Implementacion de RealtimeEmitter usando el Socket.IO Server. Todos los
 * emits pasan por rooms tenant-scoped — cierre defensivo contra emit
 * cross-tenant por bug de nombrado de canal.
 *
 * Rooms convention (must match the exact strings joined in the socket handlers):
 *   - t:{tenantId}:c:{conversationId} — chat conversation
 *   - t:{tenantId}:call:{callId}      — 1:1 call
 *   - t:{tenantId}:m:{meetingId}      — meeting
 *   - t:{tenantId}:u:{userId}         — all sockets of a single user
 *   - t:{tenantId}                    — every socket in the tenant (broadcasts)
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

  emitToCall<T>(input: {
    tenantId: string;
    callId: string;
    event: string;
    envelope: SocketEnvelope<T>;
  }): void {
    this.io.to(`t:${input.tenantId}:call:${input.callId}`).emit(input.event, input.envelope);
  }

  emitToMeeting<T>(input: {
    tenantId: string;
    meetingId: string;
    event: string;
    envelope: SocketEnvelope<T>;
  }): void {
    this.io.to(`t:${input.tenantId}:m:${input.meetingId}`).emit(input.event, input.envelope);
  }

  emitToUser<T>(input: {
    tenantId: string;
    userId: string;
    event: string;
    envelope: SocketEnvelope<T>;
  }): void {
    this.io.to(`t:${input.tenantId}:u:${input.userId}`).emit(input.event, input.envelope);
  }

  emitToTenant<T>(input: { tenantId: string; event: string; envelope: SocketEnvelope<T> }): void {
    this.io.to(`t:${input.tenantId}`).emit(input.event, input.envelope);
  }
}
