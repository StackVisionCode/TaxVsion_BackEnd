import type { FastifyInstance } from 'fastify';
import { prisma } from '../../../infrastructure/persistence/prisma-client.js';
import { redis } from '../../../infrastructure/redis/redis-client.js';
import { getRabbitContext } from '../../../infrastructure/rabbit/rabbit-connection.js';

/**
 * /health/live: el proceso responde. No revisa dependencias.
 * /health/ready: DB + Redis + Rabbit disponibles. Si falla, orchestrator no envia trafico.
 *
 * Convencion identica a los demas microservicios .NET (tags "ready").
 */
export async function registerHealthRoutes(app: FastifyInstance): Promise<void> {
  app.get('/health/live', async () => ({ status: 'ok' }));

  app.get('/health/ready', async (_request, reply) => {
    const checks: Record<string, 'ok' | string> = {};
    let healthy = true;

    try {
      await prisma.$queryRawUnsafe('SELECT 1');
      checks['sqlServer'] = 'ok';
    } catch (err) {
      healthy = false;
      checks['sqlServer'] = (err as Error).message;
    }

    try {
      const pong = await redis.ping();
      checks['redis'] = pong === 'PONG' ? 'ok' : `unexpected:${pong}`;
    } catch (err) {
      healthy = false;
      checks['redis'] = (err as Error).message;
    }

    try {
      const ctx = getRabbitContext();
      await ctx.channel.checkExchange(process.env['COMMUNICATION_RABBITMQ_EXCHANGE'] ?? 'taxvision-events');
      checks['rabbitmq'] = 'ok';
    } catch (err) {
      healthy = false;
      checks['rabbitmq'] = (err as Error).message;
    }

    return reply.code(healthy ? 200 : 503).send({ status: healthy ? 'ok' : 'degraded', checks });
  });
}
