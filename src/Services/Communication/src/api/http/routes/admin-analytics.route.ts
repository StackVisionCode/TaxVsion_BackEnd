import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { CommunicationPermissions } from '../../../domain/shared/permissions.js';
import {
  ANALYTICS_GROUP_BY,
  auditPlatformAnalyticsQuery,
  platformAnalyticsSummary,
  platformAnalyticsTenants,
  type PlatformAnalyticsDeps,
} from '../../../application/use-cases/platform-analytics-queries.js';
import { platformAnalyticsInfra, platformAnalyticsLive } from '../../../application/use-cases/platform-analytics-live.js';
import { requirePlatformPermission } from '../require-platform-permission.js';
import { logger } from '../../../infrastructure/logger/logger.js';
import type { AuthenticatedPrincipal } from '../../../infrastructure/jwks/jwt-verifier.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const Day = z.string().regex(/^\d{4}-\d{2}-\d{2}$/);
const PlanCode = z.string().min(1).max(64);

/**
 * F10 (ADD-08 T-72, D-36): analítica de plataforma sin UI, solo para PlatformAdmin con
 * `communication.platform.analytics.read`. Todo sale del snapshot por eventos (summary/tenants) o de
 * conteos y latidos puntuales (live/infra); ninguna respuesta lleva nombres ni contenido. Cada
 * consulta queda auditada. La vista de oficina sigue en `/communication/analytics/*`.
 */
export async function registerAdminAnalyticsRoutes(app: FastifyInstance, container: AppContainer, options: {
  defaultRangeDays: number;
  maxRangeDays: number;
  tenantsMaxLimit: number;
  grafanaUrl: string | null;
}): Promise<void> {
  const rangeDeps: PlatformAnalyticsDeps = {
    analytics: container.analytics,
    timeZone: async () => (await container.effectiveSettings.platform()).analyticsTimeZone,
    defaultRangeDays: options.defaultRangeDays,
    maxRangeDays: options.maxRangeDays,
  };
  const Limit = z.coerce.number().int().min(1).max(options.tenantsMaxLimit).default(Math.min(100, options.tenantsMaxLimit));

  const audit = (principal: AuthenticatedPrincipal, endpoint: string, params: Record<string, unknown>, correlationId: string): void => {
    // La auditoría no frena la respuesta; si falla queda en el log.
    void auditPlatformAnalyticsQuery(
      { endpoint, actorUserId: principal.userId, params, correlationId },
      { audit: container.configurationAudit },
    ).catch((err: unknown) => logger.warn({ err: (err as Error).message, endpoint }, 'platform analytics audit failed'));
  };

  app.get('/communication/admin/analytics/summary', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
    const denied = await requirePlatformPermission(principal, CommunicationPermissions.PlatformAnalyticsRead, container);
    if (denied) return reply.code(denied.status).send(denied.body);
    const query = z
      .object({
        from: Day.optional(),
        to: Day.optional(),
        groupBy: z.enum(ANALYTICS_GROUP_BY).optional(),
        tenantId: z.string().uuid().optional(),
        planCode: PlanCode.optional(),
      })
      .safeParse(request.query);
    if (!query.success) return reply.code(400).send({ code: 'Validation.Failed', message: query.error.issues[0]?.message ?? 'Invalid query.' });
    const q = query.data;
    const result = await platformAnalyticsSummary(
      {
        ...(q.from ? { fromDay: q.from } : {}),
        ...(q.to ? { toDay: q.to } : {}),
        ...(q.groupBy ? { groupBy: q.groupBy } : {}),
        ...(q.tenantId ? { tenantId: q.tenantId } : {}),
        ...(q.planCode ? { planCode: q.planCode } : {}),
      },
      rangeDeps,
    );
    if (!result.isSuccess) return reply.code(400).send({ code: result.error.code, message: result.error.message });
    audit(principal, 'summary', q, request.id);
    return reply.send(result.value);
  });

  app.get('/communication/admin/analytics/tenants', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
    const denied = await requirePlatformPermission(principal, CommunicationPermissions.PlatformAnalyticsRead, container);
    if (denied) return reply.code(denied.status).send(denied.body);
    const query = z
      .object({ from: Day.optional(), to: Day.optional(), planCode: PlanCode.optional(), limit: Limit })
      .safeParse(request.query);
    if (!query.success) return reply.code(400).send({ code: 'Validation.Failed', message: query.error.issues[0]?.message ?? 'Invalid query.' });
    const q = query.data;
    const result = await platformAnalyticsTenants(
      {
        ...(q.from ? { fromDay: q.from } : {}),
        ...(q.to ? { toDay: q.to } : {}),
        ...(q.planCode ? { planCode: q.planCode } : {}),
        limit: q.limit,
      },
      rangeDeps,
    );
    if (!result.isSuccess) return reply.code(400).send({ code: result.error.code, message: result.error.message });
    audit(principal, 'tenants', q, request.id);
    return reply.send(result.value);
  });

  app.get('/communication/admin/analytics/live', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
    const denied = await requirePlatformPermission(principal, CommunicationPermissions.PlatformAnalyticsRead, container);
    if (denied) return reply.code(denied.status).send(denied.body);
    const query = z.object({ limit: Limit }).safeParse(request.query);
    if (!query.success) return reply.code(400).send({ code: 'Validation.Failed', message: query.error.issues[0]?.message ?? 'Invalid query.' });
    const result = await platformAnalyticsLive(
      { limit: query.data.limit },
      { live: container.liveConcurrency, instances: container.instanceTelemetry },
    );
    audit(principal, 'live', query.data, request.id);
    return reply.send(result);
  });

  app.get('/communication/admin/analytics/infra', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
    const denied = await requirePlatformPermission(principal, CommunicationPermissions.PlatformAnalyticsRead, container);
    if (denied) return reply.code(denied.status).send(denied.body);
    const result = await platformAnalyticsInfra({ instances: container.instanceTelemetry, grafanaUrl: options.grafanaUrl });
    audit(principal, 'infra', {}, request.id);
    return reply.send(result);
  });
}
