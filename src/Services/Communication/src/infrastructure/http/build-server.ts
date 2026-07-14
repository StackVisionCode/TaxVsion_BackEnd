import Fastify, { type FastifyInstance } from 'fastify';
import cors from '@fastify/cors';
import helmet from '@fastify/helmet';
import rateLimit from '@fastify/rate-limit';
import sensible from '@fastify/sensible';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import { CORRELATION_HEADER, normalizeCorrelationId } from '../../domain/shared/correlation.js';
import { registerHealthRoutes } from '../../api/http/routes/health.route.js';
import { registerAuthPlugin } from '../../api/http/plugins/auth.plugin.js';
import { registerConversationRoutes } from '../../api/http/routes/conversations.route.js';
import { registerCallRoutes } from '../../api/http/routes/calls.route.js';
import { registerMeetingRoutes } from '../../api/http/routes/meetings.route.js';
import { registerNotificationRoutes } from '../../api/http/routes/notifications.route.js';
import { registerSupportRoutes } from '../../api/http/routes/support.route.js';
import { registerSettingsRoutes } from '../../api/http/routes/settings.route.js';
import { registerAnalyticsRoutes } from '../../api/http/routes/analytics.route.js';
import { registerUploadRoutes } from '../../api/http/routes/uploads.route.js';
import type { AppContainer } from '../container.js';

/**
 * Fastify sin batteries incluidas mas alla de lo estrictamente necesario para
 * Fase 0: health checks, CORS, helmet, rate limit, correlation, auth JWKS.
 * Cada plugin nuevo (Zod TypeProvider, routes de dominio) llegara en su fase.
 */
export async function buildHttpServer(container: AppContainer): Promise<FastifyInstance> {
  const app = Fastify({
    loggerInstance: logger,
    disableRequestLogging: false,
    trustProxy: true,
    genReqId: (req) => normalizeCorrelationId(req.headers[CORRELATION_HEADER] as string | undefined),
    requestIdHeader: CORRELATION_HEADER,
    requestIdLogLabel: 'correlationId',
    // Default de Fastify es 1 MiB — insuficiente para el upload de
    // grabaciones (uploads.route.ts). Al superarlo, Fastify corta la
    // conexion a mitad de stream en vez de responder 413 prolijo, y el
    // gateway (YARP) lo ve como conexion rota -> 502 Bad Gateway. 220MB da
    // margen sobre el limite de 200MB que aplica @fastify/multipart al
    // tamaño del archivo en si.
    bodyLimit: 220 * 1024 * 1024,
  });

  await app.register(sensible);
  await app.register(helmet, { global: true });
  await app.register(cors, {
    origin: (origin, cb) => {
      if (!origin) return cb(null, true);
      if (config.cors.origins.length === 0) return cb(null, true);
      cb(null, config.cors.origins.includes(origin));
    },
    credentials: true,
  });
  await app.register(rateLimit, {
    max: 300,
    timeWindow: '1 minute',
    keyGenerator: (req) => (req.headers['x-real-ip'] as string) ?? req.ip,
  });

  // Envia el CorrelationId siempre en la respuesta.
  app.addHook('onSend', async (req, reply) => {
    reply.header(CORRELATION_HEADER, req.id);
  });

  await app.register(registerAuthPlugin);
  await app.register(registerHealthRoutes);
  await registerConversationRoutes(app, container);
  await registerCallRoutes(app, container);
  await registerMeetingRoutes(app, container);
  await registerNotificationRoutes(app, container);
  await registerSupportRoutes(app, container);
  await registerSettingsRoutes(app, container);
  await registerAnalyticsRoutes(app, container);
  await registerUploadRoutes(app);

  return app;
}
