import type { CallRoomPresence } from '../../application/ports/abandoned-call-ports.js';
import type { CommunicationIoServer } from './build-io.js';

/** Cuenta con `fetchSockets`, que con el adaptador Redis pregunta a todas las instancias. */
export class SocketCallRoomPresence implements CallRoomPresence {
  constructor(private readonly io: CommunicationIoServer) {}

  async connectedCount(tenantId: string, callId: string): Promise<number> {
    const sockets = await this.io.in(`t:${tenantId}:call:${callId}`).fetchSockets();
    return sockets.length;
  }
}
