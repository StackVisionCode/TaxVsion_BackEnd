import { randomUUID } from 'node:crypto';
import { hostname } from 'node:os';
import type { InstanceTelemetryStore } from '../../application/ports/instance-telemetry-store.js';
import type { SfuService } from '../../application/ports/sfu-service.js';
import { logger } from '../logger/logger.js';
import { sfuParticipantsActive, sfuRoomsActive, socketConnectionsActive } from './metrics.js';

/**
 * F10 (ADD-08 T-74): cada `intervalSeconds` el proceso publica sus sockets y el uso del SFU en Redis
 * (TTL = 3 latidos) y actualiza los gauges de Prometheus. `admin/analytics/live|infra` suman lo de
 * todos los procesos; no depende de que la pila de observabilidad esté encendida.
 */
export function startInstanceHeartbeat(
  config: { intervalSeconds: number },
  deps: { store: InstanceTelemetryStore; sfu?: SfuService },
): { stop(): void; readonly instanceId: string } {
  const instanceId = randomUUID();
  const startedAtUtc = new Date().toISOString();
  const host = hostname();

  const beat = async (): Promise<void> => {
    try {
      const sockets = (await socketConnectionsActive.get()).values[0]?.value ?? 0;
      const usage = deps.sfu ? await deps.sfu.usage() : null;
      if (usage) {
        sfuRoomsActive.set(usage.rooms);
        sfuParticipantsActive.set(usage.participants);
      }
      await deps.store.publish(
        {
          instanceId,
          hostname: host,
          pid: process.pid,
          startedAtUtc,
          updatedAtUtc: new Date().toISOString(),
          sockets,
          sfu: usage && deps.sfu ? { ...usage, runtime: deps.sfu.describe() } : null,
        },
        config.intervalSeconds * 3,
      );
    } catch (err) {
      logger.warn({ err: (err as Error).message }, 'instance heartbeat failed');
    }
  };

  void beat();
  const handle = setInterval(() => void beat(), config.intervalSeconds * 1000);
  handle.unref();
  return {
    instanceId,
    stop() {
      clearInterval(handle);
    },
  };
}
