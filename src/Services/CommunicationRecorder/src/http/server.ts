import Fastify, { type FastifyInstance, type FastifyReply, type FastifyRequest } from 'fastify';
import type { Logger } from 'pino';
import type { ZodType } from 'zod';
import type { SessionManager } from '../capture/session-manager.js';
import {
  createSessionSchema,
  openTrackSchema,
  recomposeSchema,
  sessionParamsSchema,
  stopSessionSchema,
  trackParamsSchema,
} from '../contracts/api.js';
import { Errors, RecorderError } from '../contracts/errors.js';
import type { RecorderMetrics } from '../telemetry/metrics.js';
import type { ServiceAuthGuard } from './service-auth.js';

export interface ServerDeps {
  readonly sessions: SessionManager;
  readonly auth: ServiceAuthGuard;
  readonly metrics: RecorderMetrics;
  readonly queueDepth: () => Promise<number>;
  readonly logger: Logger;
  readonly bodyLimitBytes: number;
}

const CORRELATION_HEADER = 'x-correlation-id';

function parse<T>(schema: ZodType<T>, value: unknown): T {
  const result = schema.safeParse(value);
  if (!result.success) {
    const message = result.error.issues
      .slice(0, 5)
      .map((i) => `${i.path.join('.') || '(body)'}: ${i.message}`)
      .join('; ');
    throw Errors.invalidRequest(message);
  }
  return result.data;
}

/** API v1 del recorder (F7_CONTRACTS §9.1). `/health` y `/metrics` sin auth (red interna). */
export async function buildServer(deps: ServerDeps): Promise<FastifyInstance> {
  const app = Fastify({
    loggerInstance: deps.logger,
    bodyLimit: deps.bodyLimitBytes,
    requestIdHeader: CORRELATION_HEADER,
  }) as unknown as FastifyInstance;

  // Un DELETE/POST con content-type JSON y cuerpo vacío es válido aquí (Fastify lo rechaza por defecto).
  app.removeContentTypeParser('application/json');
  app.addContentTypeParser('application/json', { parseAs: 'string' }, (_req, body, done) => {
    const text = typeof body === 'string' ? body : body.toString('utf-8');
    if (text.trim() === '') return done(null, undefined);
    try {
      done(null, JSON.parse(text));
    } catch {
      done(Errors.invalidRequest('Body is not valid JSON.'), undefined);
    }
  });

  app.setErrorHandler((err: unknown, req: FastifyRequest, reply: FastifyReply) => {
    if (err instanceof RecorderError) {
      return reply.status(err.statusCode).send({ code: err.code, message: err.message });
    }
    const e = err as { statusCode?: number; message?: string; code?: string };
    if (typeof e.statusCode === 'number' && e.statusCode >= 400 && e.statusCode < 500) {
      // JSON inválido, cuerpo demasiado grande, content-type: errores del cliente de Fastify.
      return reply.status(e.statusCode).send({ code: 'Recording.InvalidRequest', message: e.message ?? 'Bad request.' });
    }
    req.log.error({ err: e.message, url: req.url }, 'unhandled recorder error');
    return reply.status(500).send({ code: 'Recording.InternalError', message: 'Unexpected recorder error.' });
  });
  app.setNotFoundHandler((_req, reply) => reply.status(404).send({ code: 'Recording.RouteNotFound', message: 'Route not found.' }));

  // Sondas frecuentes (healthcheck/Prometheus): sin log por petición.
  app.get('/health', { logLevel: 'warn' }, async () => {
    const queueDepth = await deps.queueDepth().catch(() => null);
    return {
      status: 'ok',
      activeSessions: deps.sessions.activeSessions,
      activeTracks: deps.sessions.activeTracks,
      queueDepth,
    };
  });

  app.get('/metrics', { logLevel: 'warn' }, async (_req, reply) => {
    const depth = await deps.queueDepth().catch(() => null);
    if (depth !== null) deps.metrics.queueDepth.set(depth);
    reply.header('Content-Type', deps.metrics.registry.contentType);
    return reply.send(await deps.metrics.registry.metrics());
  });

  await app.register(
    async (v1) => {
      v1.addHook('onRequest', async (req) => {
        await deps.auth(req.headers.authorization);
      });

      v1.post('/sessions', async (req, reply) => {
        const body = parse(createSessionSchema, req.body);
        const header = req.headers[CORRELATION_HEADER];
        const correlationId = typeof header === 'string' && header.length > 0 ? header : undefined;
        const { created } = await deps.sessions.createSession(body, correlationId);
        return reply.status(created ? 201 : 200).send({ sessionId: body.sessionId });
      });

      v1.post('/sessions/:id/tracks', async (req, reply) => {
        const { id } = parse(sessionParamsSchema, req.params);
        const body = parse(openTrackSchema, req.body);
        const result = await deps.sessions.openTrack(id, body);
        return reply.status(result.created ? 201 : 200).send({ ip: result.ip, port: result.port, rtcpPort: result.rtcpPort });
      });

      v1.delete('/sessions/:id/tracks/:trackId', async (req, reply) => {
        const { id, trackId } = parse(trackParamsSchema, req.params);
        await deps.sessions.closeTrack(id, trackId);
        return reply.status(204).send();
      });

      v1.post('/sessions/:id/stop', async (req, reply) => {
        const { id } = parse(sessionParamsSchema, req.params);
        const body = parse(stopSessionSchema, req.body);
        return reply.status(202).send(await deps.sessions.stopSession(id, body));
      });

      v1.post('/sessions/:id/recompose', async (req, reply) => {
        const { id } = parse(sessionParamsSchema, req.params);
        const body = parse(recomposeSchema, req.body);
        return reply.status(202).send(await deps.sessions.recompose(id, body));
      });

      v1.delete('/sessions/:id', async (req, reply) => {
        const { id } = parse(sessionParamsSchema, req.params);
        await deps.sessions.abortSession(id);
        return reply.status(204).send();
      });
    },
    { prefix: '/v1' },
  );

  return app;
}
