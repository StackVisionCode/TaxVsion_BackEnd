import { createSocket } from 'node:dgram';

export interface PortPair {
  readonly rtpPort: number;
  readonly rtcpPort: number;
}

export type PortProbe = (port: number) => Promise<boolean>;

export class PortsExhaustedError extends Error {
  constructor() {
    super('No free RTP/RTCP port pair in range');
    this.name = 'PortsExhaustedError';
  }
}

/**
 * Pares RTP/RTCP (RTP par, RTCP = RTP + 1) de un rango. Reparte en round-robin para no reutilizar de
 * inmediato un puerto recién liberado (paquetes tardíos del consumer anterior) y, si hay sonda, salta
 * los pares que otro proceso ya ocupa (en local el rango de respaldo de mediasoup se solapa).
 */
export class PortAllocator {
  private readonly used = new Set<number>();
  private readonly first: number;
  private readonly pairs: number;
  private cursor = 0;

  constructor(
    min: number,
    max: number,
    private readonly probe?: PortProbe,
  ) {
    this.first = min % 2 === 0 ? min : min + 1;
    this.pairs = Math.max(0, Math.floor((max - this.first + 1) / 2));
  }

  get capacity(): number {
    return this.pairs;
  }

  get inUse(): number {
    return this.used.size;
  }

  async allocate(): Promise<PortPair> {
    for (let i = 0; i < this.pairs; i += 1) {
      const rtpPort = this.first + ((this.cursor + i) % this.pairs) * 2;
      if (this.used.has(rtpPort)) continue;
      // Se reserva antes de la sonda (async) para que dos aperturas concurrentes no tomen el mismo par.
      this.used.add(rtpPort);
      const free = this.probe ? (await this.probe(rtpPort)) && (await this.probe(rtpPort + 1)) : true;
      if (!free) {
        this.used.delete(rtpPort);
        continue;
      }
      this.cursor = (this.cursor + i + 1) % this.pairs;
      return { rtpPort, rtcpPort: rtpPort + 1 };
    }
    throw new PortsExhaustedError();
  }

  release(rtpPort: number): void {
    this.used.delete(rtpPort);
  }
}

/** Sonda real: intenta enlazar UDP en la IP de escucha y suelta el socket. */
export function udpPortProbe(listenIp: string): PortProbe {
  return (port) =>
    new Promise((resolve) => {
      const socket = createSocket(listenIp.includes(':') ? 'udp6' : 'udp4');
      socket.once('error', () => {
        try {
          socket.close();
        } catch {
          // el socket nunca llegó a enlazarse
        }
        resolve(false);
      });
      socket.bind({ port, address: listenIp, exclusive: true }, () => {
        socket.close(() => resolve(true));
      });
    });
}
