import { describe, it, expect, afterEach } from 'vitest';
import { request } from 'node:http';
import type { Server } from 'node:http';
import { startMetricsServer, stopMetricsServer } from '../../src/telemetry/metrics-server.js';

/**
 * Fase Transcript 8 — servidor HTTP minimo dedicado a /metrics. `port: 0` deja
 * que el OS asigne un puerto libre (evita colisiones si corre en paralelo con
 * otro test o con el proceso real del worker en la misma maquina).
 */

function get(port: number, path: string): Promise<{ status: number; contentType: string | undefined; body: string }> {
  return new Promise((resolve, reject) => {
    const req = request({ host: '127.0.0.1', port, path, method: 'GET' }, (res) => {
      let body = '';
      res.on('data', (chunk: Buffer) => (body += chunk.toString('utf-8')));
      res.on('end', () =>
        resolve({ status: res.statusCode ?? 0, contentType: res.headers['content-type'], body }),
      );
    });
    req.on('error', reject);
    req.end();
  });
}

let server: Server | undefined;

afterEach(async () => {
  if (server) {
    await stopMetricsServer(server);
    server = undefined;
  }
});

describe('metrics HTTP server', () => {
  it('GET /metrics devuelve 200 con el contentType de prom-client y expone los metrics registrados', async () => {
    server = await startMetricsServer(0);
    const port = (server.address() as { port: number }).port;

    const res = await get(port, '/metrics');

    expect(res.status).toBe(200);
    expect(res.contentType).toMatch(/^text\/plain/);
    expect(res.body).toContain('transcript_worker_pipeline_failures_total');
    expect(res.body).toContain('transcript_worker_pipeline_duration_seconds');
  });

  it('cualquier otra ruta devuelve 404', async () => {
    server = await startMetricsServer(0);
    const port = (server.address() as { port: number }).port;

    const res = await get(port, '/nope');

    expect(res.status).toBe(404);
  });
});
