import type { CommunicationIoServer } from './build-io.js';
import type { SocketSessionTerminator } from '../../application/ports/socket-session-terminator.js';
import { logger } from '../logger/logger.js';

/** Desconecta (con cierre del transporte) todos los sockets de la room de usuario `t:{tenant}:u:{userId}`. */
export class SocketIoSessionTerminator implements SocketSessionTerminator {
  constructor(private readonly io: CommunicationIoServer) {}

  async disconnectUser(tenantId: string, userId: string): Promise<void> {
    try {
      this.io.in(`t:${tenantId}:u:${userId}`).disconnectSockets(true);
    } catch (err) {
      logger.warn({ err, tenantId, userId }, 'disconnectUser failed');
    }
  }
}
