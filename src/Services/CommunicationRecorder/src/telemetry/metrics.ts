import { Counter, Gauge, Histogram, Registry, collectDefaultMetrics } from 'prom-client';

/** Registry propio (no el global) para no pisarse con otros módulos y poder aislarlo en tests. */
export function createMetrics(withDefaults = true) {
  const registry = new Registry();
  if (withDefaults) collectDefaultMetrics({ register: registry });
  return {
    registry,
    activeSessions: new Gauge({
      name: 'recorder_active_sessions',
      help: 'Sesiones capturando o esperando stop.',
      registers: [registry],
    }),
    activeTracks: new Gauge({
      name: 'recorder_active_tracks',
      help: 'Pistas con un ffmpeg de captura vivo.',
      registers: [registry],
    }),
    queueDepth: new Gauge({
      name: 'recorder_compose_queue_depth',
      help: 'Trabajos de composición pendientes + en curso.',
      registers: [registry],
    }),
    composeDurationSeconds: new Histogram({
      name: 'recorder_compose_duration_seconds',
      help: 'Tiempo de pared de una composición (segmentos + mux).',
      labelNames: ['format'] as const,
      buckets: [5, 15, 30, 60, 120, 300, 600, 1200, 2400],
      registers: [registry],
    }),
    composeRealtimeFactor: new Histogram({
      name: 'recorder_compose_realtime_factor',
      help: 'Duración del medio / tiempo de pared de la composición (más alto = más rápido).',
      buckets: [0.5, 1, 2, 4, 8, 12, 20, 40],
      registers: [registry],
    }),
    compositionsTotal: new Counter({
      name: 'recorder_compositions_total',
      help: 'Composiciones publicadas.',
      labelNames: ['format'] as const,
      registers: [registry],
    }),
    failuresTotal: new Counter({
      name: 'recorder_composition_failures_total',
      help: 'composition_failed publicados, por motivo.',
      labelNames: ['reason'] as const,
      registers: [registry],
    }),
  };
}

export type RecorderMetrics = ReturnType<typeof createMetrics>;
