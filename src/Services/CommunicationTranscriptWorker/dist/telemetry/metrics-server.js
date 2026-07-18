import { createServer } from 'node:http';
import { metricsRegistry } from './metrics.js';
import { logger } from '../logger.js';
/**
 * Fase Transcript 8 — este proceso es un consumer puro (sin API HTTP alguna),
 * asi que no hay framework (fastify/express) del que colgar una ruta. En vez
 * de traer una dependencia entera solo para 1 endpoint, un servidor
 * `node:http` minimo alcanza: unicamente sirve `GET /metrics` en texto plano
 * para que Prometheus haga scrape; cualquier otra ruta devuelve 404.
 */
export function startMetricsServer(port) {
    return new Promise((resolve, reject) => {
        const server = createServer((req, res) => {
            if (req.method === 'GET' && req.url === '/metrics') {
                metricsRegistry
                    .metrics()
                    .then((body) => {
                    res.writeHead(200, { 'Content-Type': metricsRegistry.contentType });
                    res.end(body);
                })
                    .catch((err) => {
                    logger.error({ err: err.message }, 'failed to collect metrics');
                    res.writeHead(500);
                    res.end();
                });
                return;
            }
            res.writeHead(404);
            res.end();
        });
        server.once('error', reject);
        server.listen(port, () => {
            server.removeListener('error', reject);
            logger.info({ port }, 'metrics server listening on /metrics');
            resolve(server);
        });
    });
}
export function stopMetricsServer(server) {
    return new Promise((resolve, reject) => {
        server.close((err) => (err ? reject(err) : resolve()));
    });
}
//# sourceMappingURL=metrics-server.js.map