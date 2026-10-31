import type { InstanceTelemetryStore } from '../ports/instance-telemetry-store.js';
import type { LiveConcurrency, LiveConcurrencyReader } from '../ports/live-concurrency-reader.js';
import type { SfuRuntimeInfo, SfuWorkerUsage } from '../ports/sfu-service.js';

/** F10 (ADD-08 T-72/T-74): concurrencia de la plataforma en este instante. */
export interface PlatformAnalyticsLive {
  readonly atUtc: string;
  readonly meetings: number;
  readonly participants: number;
  /** Suma de los sockets de todos los procesos con latido vigente. */
  readonly sockets: number;
  readonly instances: ReadonlyArray<{
    readonly instanceId: string;
    readonly hostname: string;
    readonly pid: number;
    readonly startedAtUtc: string;
    readonly updatedAtUtc: string;
    readonly sockets: number;
    readonly sfuRooms: number;
    readonly sfuParticipants: number;
  }>;
  readonly totalTenants: number;
  readonly tenants: ReadonlyArray<LiveConcurrency & { readonly tenantId: string }>;
}

/** F10 (ADD-08 T-74): uso del SFU por proceso y worker, y dónde mirar la pila si está encendida. */
export interface PlatformAnalyticsInfra {
  readonly atUtc: string;
  readonly totals: {
    readonly instances: number;
    readonly workers: number;
    readonly fallbackWorkers: number;
    readonly rooms: number;
    readonly participants: number;
  };
  readonly instances: ReadonlyArray<{
    readonly instanceId: string;
    readonly hostname: string;
    readonly pid: number;
    readonly updatedAtUtc: string;
    readonly sfu: {
      readonly runtime: SfuRuntimeInfo;
      readonly rooms: number;
      readonly participants: number;
      readonly workers: readonly SfuWorkerUsage[];
    } | null;
  }>;
  readonly observability: {
    /** Enlace a Grafana cuando la pila (perfil `observability`) está encendida y configurada. */
    readonly grafanaUrl: string | null;
    readonly metricsPath: string;
  };
}

export async function platformAnalyticsLive(
  input: { limit: number },
  deps: { live: LiveConcurrencyReader; instances: InstanceTelemetryStore; now?: () => Date },
): Promise<PlatformAnalyticsLive> {
  const [live, instances] = await Promise.all([deps.live.countPlatform(), deps.instances.list()]);
  return {
    atUtc: (deps.now ?? (() => new Date()))().toISOString(),
    meetings: live.meetings,
    participants: live.participants,
    sockets: instances.reduce((sum, i) => sum + i.sockets, 0),
    instances: instances.map((i) => ({
      instanceId: i.instanceId,
      hostname: i.hostname,
      pid: i.pid,
      startedAtUtc: i.startedAtUtc,
      updatedAtUtc: i.updatedAtUtc,
      sockets: i.sockets,
      sfuRooms: i.sfu?.rooms ?? 0,
      sfuParticipants: i.sfu?.participants ?? 0,
    })),
    totalTenants: live.byTenant.length,
    tenants: live.byTenant.slice(0, input.limit),
  };
}

export async function platformAnalyticsInfra(deps: {
  instances: InstanceTelemetryStore;
  grafanaUrl: string | null;
  now?: () => Date;
}): Promise<PlatformAnalyticsInfra> {
  const instances = await deps.instances.list();
  const withSfu = instances.filter((i) => i.sfu !== null);
  return {
    atUtc: (deps.now ?? (() => new Date()))().toISOString(),
    totals: {
      instances: instances.length,
      workers: withSfu.reduce((sum, i) => sum + (i.sfu?.workers.length ?? 0), 0),
      fallbackWorkers: withSfu.reduce((sum, i) => sum + (i.sfu?.runtime.fallbackWorkers ?? 0), 0),
      rooms: withSfu.reduce((sum, i) => sum + (i.sfu?.rooms ?? 0), 0),
      participants: withSfu.reduce((sum, i) => sum + (i.sfu?.participants ?? 0), 0),
    },
    instances: instances.map((i) => ({
      instanceId: i.instanceId,
      hostname: i.hostname,
      pid: i.pid,
      updatedAtUtc: i.updatedAtUtc,
      sfu: i.sfu ? { runtime: i.sfu.runtime, rooms: i.sfu.rooms, participants: i.sfu.participants, workers: i.sfu.workers } : null,
    })),
    observability: { grafanaUrl: deps.grafanaUrl, metricsPath: '/metrics' },
  };
}
