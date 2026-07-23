import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { checkPermission, permissionCheckHttpStatus, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import { analyticsSummary, analyticsTimeline } from '../../../application/use-cases/analytics-queries.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const RangeQuery = z.object({
  from: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/)
    .optional(),
  to: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/)
    .optional(),
});

export async function registerAnalyticsRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get('/communication/analytics/summary', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const permCheck = await checkPermission(principal, CommunicationPermissions.AnalyticsRead, container.userPermissions);
    if (!permCheck.allowed) {
      return reply.code(permissionCheckHttpStatus(permCheck)).send({ code: permCheck.code, message: permCheck.message });
    }
    const query = RangeQuery.parse(request.query);
    const result = await analyticsSummary(
      {
        tenantId: principal.tenantId,
        ...(query.from !== undefined ? { fromDay: query.from } : {}),
        ...(query.to !== undefined ? { toDay: query.to } : {}),
      },
      container,
    );
    return reply.send(result);
  });

  app.get('/communication/analytics/timeline', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const permCheck = await checkPermission(principal, CommunicationPermissions.AnalyticsRead, container.userPermissions);
    if (!permCheck.allowed) {
      return reply.code(permissionCheckHttpStatus(permCheck)).send({ code: permCheck.code, message: permCheck.message });
    }
    const query = RangeQuery.parse(request.query);
    const result = await analyticsTimeline(
      {
        tenantId: principal.tenantId,
        ...(query.from !== undefined ? { fromDay: query.from } : {}),
        ...(query.to !== undefined ? { toDay: query.to } : {}),
      },
      container,
    );
    return reply.send(result);
  });
}
