import { Registry, collectDefaultMetrics, Counter, Histogram, Gauge } from 'prom-client';

/**
 * Fase Backend 11 — `prom-client` estaba en package.json desde antes (backlog
 * #229) pero nunca se uso. Registry propio (no el global default de la
 * libreria) para no pisarse con otro modulo que tambien use prom-client y para
 * poder limpiarlo en tests si hiciera falta.
 */
export const metricsRegistry = new Registry();

collectDefaultMetrics({ register: metricsRegistry });

export const httpRequestsTotal = new Counter({
  name: 'communication_http_requests_total',
  help: 'Total de requests HTTP procesadas, por metodo/ruta/status.',
  labelNames: ['method', 'route', 'status'] as const,
  registers: [metricsRegistry],
});

export const httpRequestDurationSeconds = new Histogram({
  name: 'communication_http_request_duration_seconds',
  help: 'Duracion de requests HTTP en segundos, por metodo/ruta/status.',
  labelNames: ['method', 'route', 'status'] as const,
  buckets: [0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10],
  registers: [metricsRegistry],
});

export const socketConnectionsActive = new Gauge({
  name: 'communication_socket_connections_active',
  help: 'Conexiones Socket.IO activas en este proceso (no cluster-wide).',
  registers: [metricsRegistry],
});
