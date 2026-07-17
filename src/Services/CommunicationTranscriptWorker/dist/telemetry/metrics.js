import { Registry, collectDefaultMetrics, Counter, Histogram } from 'prom-client';
/**
 * Fase Transcript 8 — Registry propio (no el global de la libreria), mismo
 * criterio que Communication (infrastructure/telemetry/metrics.ts): evita
 * pisarse con otro modulo que tambien use prom-client y permite resetear en
 * tests sin efectos globales.
 */
export const metricsRegistry = new Registry();
collectDefaultMetrics({ register: metricsRegistry });
export const pipelineFailuresTotal = new Counter({
    name: 'transcript_worker_pipeline_failures_total',
    help: 'Total de fallas del pipeline de transcripcion, por stage (failureReason) y tipo de grabacion.',
    labelNames: ['reason', 'kind'],
    registers: [metricsRegistry],
});
export const pipelineDurationSeconds = new Histogram({
    name: 'transcript_worker_pipeline_duration_seconds',
    help: 'Duracion total de processRecordingReady (download+ffmpeg+whisper+upload+publish), por tipo de grabacion y resultado.',
    labelNames: ['kind', 'status'],
    // El pipeline es CPU-bound (whisper) y depende de la duracion de la
    // grabacion original — buckets pensados para minutos, no milisegundos.
    buckets: [1, 5, 15, 30, 60, 120, 300, 600, 1200],
    registers: [metricsRegistry],
});
//# sourceMappingURL=metrics.js.map