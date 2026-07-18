import Fastify, { type FastifyInstance } from 'fastify';
import cors from '@fastify/cors';
import helmet from '@fastify/helmet';
import rateLimit from '@fastify/rate-limit';
import sensible from '@fastify/sensible';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import { CORRELATION_HEADER, normalizeCorrelationId } from '../../domain/shared/correlation.js';
import { metricsRegistry, httpRequestsTotal, httpRequestDurationSeconds } from '../telemetry/metrics.js';
import { registerHealthRoutes } from '../../api/http/routes/health.route.js';
import { registerAuthPlugin } from '../../api/http/plugins/auth.plugin.js';
import { registerConversationRoutes } from '../../api/http/routes/conversations.route.js';
import { registerCallRoutes } from '../../api/http/routes/calls.route.js';
import { registerMeetingRoutes } from '../../api/http/routes/meetings.route.js';
import { registerMeetingInvitationRoutes } from '../../api/http/routes/meeting-invitations.route.js';
import { registerDirectoryRoutes } from '../../api/http/routes/directory.route.js';
import { registerNotificationRoutes } from '../../api/http/routes/notifications.route.js';
import { registerSupportRoutes } from '../../api/http/routes/support.route.js';
import { registerSettingsRoutes } from '../../api/http/routes/settings.route.js';
import { registerAnalyticsRoutes } from '../../api/http/routes/analytics.route.js';
import type { AppContainer } from '../container.js';

/**
 * Fastify sin batteries incluidas mas alla de lo estrictamente necesario para
 * Fase 0: health checks, CORS, helmet, rate limit, correlation, auth JWKS.
 * Cada plugin nuevo (Zod TypeProvider, routes de dominio) llegara en su fase.
 */
export async function buildHttpServer(container: AppContainer): Promise<FastifyInstance> {
  // `loggerInstance: logger` (pino.Logger concreto) hace que Fastify infiera
  // FastifyInstance<..., Logger> en vez de FastifyInstance<..., FastifyBaseLogger>.
  // Bajo exactOptionalPropertyTypes (tsconfig.json) esos dos tipos dejan de ser
  // mutuamente asignables por varianza de parametros en route(), aunque en
  // runtime el logger pasado SI implementa FastifyBaseLogger (es su superset).
  // El cast widening es solo para TS; no cambia el logger real que usa Fastify.
  const app = Fastify({
    loggerInstance: logger,
    disableRequestLogging: false,
    trustProxy: true,
    genReqId: (req) => normalizeCorrelationId(req.headers[CORRELATION_HEADER] as string | undefined),
    requestIdHeader: CORRELATION_HEADER,
    requestIdLogLabel: 'correlationId',
  }) as unknown as FastifyInstance;

  await app.register(sensible);
  await app.register(helmet, { global: true });
  await app.register(cors, {
    origin: (origin, cb) => {
      if (!origin) return cb(null, true);
      // `config.cors.origins` vacio solo es alcanzable en development/test —
      // config.ts (Fase Backend 11) revienta al boot si NODE_ENV=production y
      // COMMUNICATION_CORS_ORIGINS esta vacio, asi que este allow-all nunca
      // corre en produccion.
      if (config.cors.origins.length === 0) return cb(null, true);
      cb(null, config.cors.origins.includes(origin));
    },
    credentials: true,
  });
  await app.register(rateLimit, {
    max: config.rateLimit.httpGlobal.maxPerWindow,
    timeWindow: `${config.rateLimit.httpGlobal.windowSeconds} seconds`,
    keyGenerator: (req) => (req.headers['x-real-ip'] as string) ?? req.ip,
  });

  // Fase Backend 11 — metricas OTel reales (prom-client, antes instalado sin
  // usar). `routeOptions.url` es el patron de ruta ("/communication/conversations/:id"),
  // no la URL cruda — evita cardinalidad explosiva por UUIDs en el path label.
  app.addHook('onRequest', async (req) => {
    (req as unknown as { metricsStartHrTime: bigint }).metricsStartHrTime = process.hrtime.bigint();
  });
  app.addHook('onResponse', async (req, reply) => {
    const route = req.routeOptions?.url ?? req.url;
    const status = String(reply.statusCode);
    const method = req.method;
    httpRequestsTotal.labels(method, route, status).inc();
    const start = (req as unknown as { metricsStartHrTime?: bigint }).metricsStartHrTime;
    if (start !== undefined) {
      const seconds = Number(process.hrtime.bigint() - start) / 1e9;
      httpRequestDurationSeconds.labels(method, route, status).observe(seconds);
    }
  });

  // Envia el CorrelationId siempre en la respuesta.
  app.addHook('onSend', async (req, reply) => {
    reply.header(CORRELATION_HEADER, req.id);
  });

  await app.register(registerAuthPlugin);
  await app.register(registerHealthRoutes);
  // Sin auth, mismo criterio que /health — scrape interno (Prometheus), no
  // expone datos de tenant/usuario, solo contadores agregados del proceso.
  app.get('/metrics', async (_request, reply) => {
    reply.header('Content-Type', metricsRegistry.contentType);
    return reply.send(await metricsRegistry.metrics());
  });
  await registerConversationRoutes(app, container);
  await registerCallRoutes(app, container);
  await registerMeetingRoutes(app, container);
  await registerMeetingInvitationRoutes(app, container);
  await registerDirectoryRoutes(app, container);
  await registerNotificationRoutes(app, container);
  await registerSupportRoutes(app, container);
  await registerSettingsRoutes(app, container);
  await registerAnalyticsRoutes(app, container);

  return app;
}
