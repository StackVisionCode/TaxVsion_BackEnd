import { describe, it, expect, beforeEach } from 'vitest';
import { metricsRegistry, pipelineFailuresTotal, pipelineDurationSeconds } from '../../src/telemetry/metrics.js';

/**
 * Fase Transcript 8 — pruebas del modulo de metrics en aislamiento (sin pasar
 * por el pipeline real): confirma que el Registry propio expone los 2
 * metrics nuevos con el nombre/labels esperados y que se acumulan
 * correctamente via la API de prom-client.
 */

beforeEach(() => {
  pipelineFailuresTotal.reset();
  pipelineDurationSeconds.reset();
});

describe('metricsRegistry', () => {
  it('registra transcript_worker_pipeline_failures_total y transcript_worker_pipeline_duration_seconds', () => {
    const names = metricsRegistry.getMetricsAsArray().map((m) => m.name);
    expect(names).toContain('transcript_worker_pipeline_failures_total');
    expect(names).toContain('transcript_worker_pipeline_duration_seconds');
  });

  it('expone contentType valido para el endpoint /metrics', () => {
    expect(metricsRegistry.contentType).toMatch(/^text\/plain/);
  });
});

describe('pipelineFailuresTotal', () => {
  it('acumula por reason y kind independientemente', async () => {
    pipelineFailuresTotal.inc({ reason: 'DownloadFailed', kind: 'call' });
    pipelineFailuresTotal.inc({ reason: 'DownloadFailed', kind: 'call' });
    pipelineFailuresTotal.inc({ reason: 'NoAudioStream', kind: 'meeting' });

    const metric = await pipelineFailuresTotal.get();
    const downloadCall = metric.values.find(
      (v) => v.labels.reason === 'DownloadFailed' && v.labels.kind === 'call',
    );
    const noAudioMeeting = metric.values.find(
      (v) => v.labels.reason === 'NoAudioStream' && v.labels.kind === 'meeting',
    );

    expect(downloadCall?.value).toBe(2);
    expect(noAudioMeeting?.value).toBe(1);
  });
});

describe('pipelineDurationSeconds', () => {
  it('registra una observacion por invocacion de startTimer/stop, con labels kind+status', async () => {
    const stopSuccess = pipelineDurationSeconds.startTimer({ kind: 'meeting' });
    stopSuccess({ status: 'success' });

    const stopFailure = pipelineDurationSeconds.startTimer({ kind: 'call' });
    stopFailure({ status: 'failure' });

    const metric = await pipelineDurationSeconds.get();
    const successCount = metric.values.find(
      (v) =>
        v.metricName === 'transcript_worker_pipeline_duration_seconds_count' &&
        v.labels.kind === 'meeting' &&
        v.labels.status === 'success',
    );
    const failureCount = metric.values.find(
      (v) =>
        v.metricName === 'transcript_worker_pipeline_duration_seconds_count' &&
        v.labels.kind === 'call' &&
        v.labels.status === 'failure',
    );

    expect(successCount?.value).toBe(1);
    expect(failureCount?.value).toBe(1);
  });
});
