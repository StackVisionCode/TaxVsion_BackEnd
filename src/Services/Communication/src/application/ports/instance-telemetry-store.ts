import type { SfuRuntimeInfo, SfuUsage } from './sfu-service.js';

/**
 * F10 (ADD-08 T-74): lo que cada proceso de Communication publica de sí mismo en un latido. Sirve
 * para sumar sockets y SFU entre procesos sin la pila de observabilidad encendida. Sin PII.
 */
export interface InstanceSnapshot {
  readonly instanceId: string;
  readonly hostname: string;
  readonly pid: number;
  readonly startedAtUtc: string;
  readonly updatedAtUtc: string;
  readonly sockets: number;
  readonly sfu: (SfuUsage & { readonly runtime: SfuRuntimeInfo }) | null;
}

export interface InstanceTelemetryStore {
  /** Publica con caducidad: un proceso caído desaparece solo al vencer su TTL. */
  publish(snapshot: InstanceSnapshot, ttlSeconds: number): Promise<void>;
  list(): Promise<readonly InstanceSnapshot[]>;
}
