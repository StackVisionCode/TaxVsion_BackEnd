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
    volatile?: boolean;
  }): void {
    const room = this.io.to(`t:${input.tenantId}:m:${input.meetingId}`);
    (input.volatile ? room.volatile : room).emit(input.event, input.envelope);
  }

  removeUserFromConversation(input: { tenantId: string; userId: string; conversationId: string }): void {
    this.io.in(`t:${input.tenantId}:u:${input.userId}`).socketsLeave(`t:${input.tenantId}:c:${input.conversationId}`);
  }

  addUserToConversation(input: { tenantId: string; userId: string; conversationId: string }): void {
    this.io.in(`t:${input.tenantId}:u:${input.userId}`).socketsJoin(`t:${input.tenantId}:c:${input.conversationId}`);
  }

  emitToUser<T>(input: {
    tenantId: string;
    userId: string;
    event: string;
    envelope: SocketEnvelope<T>;
  }): void {
    this.io.to(`t:${input.tenantId}:u:${input.userId}`).emit(input.event, input.envelope);
  }

  /**
   * Emite a UN socket concreto (su id). Solo lo usa la capa API para la señalizacion mesh dirigida: con
   * varias pestañas, solo la que esta en la reunion recibe ofertas/candidatos (INV-02 H-9). Queda fuera
   * del puerto `RealtimeEmitter` porque los casos de uso no conocen sockets.
   */
  emitToSocket<T>(input: { socketId: string; event: string; envelope: SocketEnvelope<T> }): void {
    // Socket.IO une cada socket a una room con su propio id, asi que `to(id)` llega solo a el.
    this.io.to(input.socketId).emit(input.event, input.envelope);
  }

  emitToTenant<T>(input: { tenantId: string; event: string; envelope: SocketEnvelope<T> }): void {
    this.io.to(`t:${input.tenantId}`).emit(input.event, input.envelope);
  }

  emitToTenantStaff<T>(input: { tenantId: string; event: string; envelope: SocketEnvelope<T> }): void {
    this.io.to(`t:${input.tenantId}:staff`).emit(input.event, input.envelope);
  }

  emitToTenantMembers<T>(input: { tenantId: string; event: string; envelope: SocketEnvelope<T> }): void {
    this.io.to(`t:${input.tenantId}:members`).emit(input.event, input.envelope);
  }

  // CAT-F5.2 — Broadcast tenant-agnostico a TODOS los sockets conectados. Reservado para catalog
  // changes (planes/addons) que afectan a cualquiera que mire la lista.
  emitBroadcast<T>(input: { event: string; envelope: SocketEnvelope<T> }): void {
    this.io.emit(input.event, input.envelope);
  }
}
