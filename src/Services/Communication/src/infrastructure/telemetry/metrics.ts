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

// F10 (ADD-08): proyecciones de analítica que fallaron (el mensaje no se reintenta por ellas).
export const consumerProjectionFailuresTotal = new Counter({
  name: 'communication_consumer_projection_failures_total',
  help: 'Proyecciones de lectura (analitica) que fallaron tras el handler principal, por tipo de evento.',
  labelNames: ['event_type'] as const,
  registers: [metricsRegistry],
});

export const socketConnectionsActive = new Gauge({
  name: 'communication_socket_connections_active',
  help: 'Conexiones Socket.IO activas en este proceso (no cluster-wide).',
  registers: [metricsRegistry],
});

// F1 — transporte del SFU observable: fallos ICE/DTLS por direccion y workers sin WebRtcServer.
export const sfuTransportFailuresTotal = new Counter({
  name: 'communication_sfu_transport_failures_total',
  help: 'Transports WebRTC del SFU que llegaron a ICE/DTLS failed, por direccion y capa.',
  labelNames: ['direction', 'layer'] as const,
  registers: [metricsRegistry],
});

export const sfuTransportsConnectedTotal = new Counter({
  name: 'communication_sfu_transports_connected_total',
  help: 'Transports WebRTC del SFU que completaron ICE, por direccion y protocolo seleccionado.',
  labelNames: ['direction', 'protocol'] as const,
  registers: [metricsRegistry],
});

// F10 (ADD-08 T-74): salas y participantes del SFU en este proceso (los actualiza el latido).
export const sfuRoomsActive = new Gauge({
  name: 'communication_sfu_rooms_active',
  help: 'Salas del SFU abiertas en este proceso.',
  registers: [metricsRegistry],
});

export const sfuParticipantsActive = new Gauge({
  name: 'communication_sfu_participants_active',
  help: 'Participantes con media en el SFU de este proceso.',
  registers: [metricsRegistry],
});

export const sfuWebRtcServerFallbackWorkers = new Gauge({
  name: 'communication_sfu_webrtcserver_fallback_workers',
  help: 'Workers de mediasoup que no pudieron enlazar su WebRtcServer y usan el rango de puertos.',
  registers: [metricsRegistry],
});

/**
 * F11 (E-259): etiqueta `route` de las métricas HTTP. Sin ruta registrada (404) va todo a "unmatched":
 * la URL cruda crearía una serie por cada path inventado (memoria sin techo y URLs con query en /metrics).
 */
export function routeLabelOf(registeredRoute: string | undefined): string {
  return registeredRoute ?? 'unmatched';
}

