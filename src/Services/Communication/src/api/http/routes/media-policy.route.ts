import type { FastifyInstance } from 'fastify';
import { getMediaPolicy } from '../../../application/use-cases/media-policy-query.js';
import type { AppContainer } from '../../../infrastructure/container.js';

/** F4: cualquier principal autenticado (staff, portal, guest) lee la politica de media vigente para el. */
export async function registerMediaPolicyRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get('/communication/media-policy', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    return reply.send(await getMediaPolicy({ tenantId: principal.tenantId, userId: principal.userId }, container));
  });
}
