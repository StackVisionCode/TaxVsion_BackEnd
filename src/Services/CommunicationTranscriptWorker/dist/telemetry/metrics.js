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
// F7 — tiempo de whisper / segundos de audio. Criterio D-26: <= 0,5 con la concurrencia prevista.
export const realtimeFactor = new Histogram({
    name: 'transcript_worker_realtime_factor',
    help: 'Segundos de proceso de whisper por segundo de audio transcrito, por tipo de grabacion y modelo.',
    labelNames: ['kind', 'model'],
    buckets: [0.05, 0.1, 0.2, 0.3, 0.5, 0.75, 1, 1.5, 2, 4],
    registers: [metricsRegistry],
});
// F7 — palabras/segundo fuera de rango: low = grabacion vacia, high = alucinacion.
export const wordsPerSecondAnomaliesTotal = new Counter({
    name: 'transcript_worker_wps_anomalies_total',
    help: 'Transcripts con palabras/segundo fuera del rango configurado (TRANSCRIPT_WORKER_WPS_MIN/MAX).',
    labelNames: ['kind', 'direction'],
    registers: [metricsRegistry],
});
// F7 — rechazos por duracion (INV-12 H-1). Tambien cuentan en pipelineFailuresTotal{reason="TooLong"}.
export const tooLongTotal = new Counter({
    name: 'transcript_worker_too_long_total',
    help: 'Grabaciones rechazadas por superar maxDurationSeconds + tolerancia.',
    labelNames: ['kind'],
    registers: [metricsRegistry],
});
//# sourceMappingURL=metrics.js.map