import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { issueIceCredentials } from '../../../application/use-cases/issue-ice-credentials.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const IceQuerySchema = z.object({
  ttl: z.coerce.number().int().min(60).max(3600).optional(),
});

const HistoryQuerySchema = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
});

const CustomerCallsParamsSchema = z.object({
  customerId: z.string().uuid(),
});

const CustomerCallsQuerySchema = z.object({
  // Historial por cliente: por defecto traemos hasta 100 (los volúmenes por cliente son bajos) para
  // poder calcular stats exactas (total/completed/missed/avg) sobre el conjunto devuelto.
  size: z.coerce.number().int().min(1).max(200).default(100),
});

export async function registerCallRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  // GET /communication/webrtc/ice
  app.get('/communication/webrtc/ice', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const query = IceQuerySchema.parse(request.query);
    const result = issueIceCredentials(
      {
        tenantId: principal.tenantId,
        userId: principal.userId,
        ...(query.ttl !== undefined ? { ttlSeconds: query.ttl } : {}),
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.send(result.value);
  });

  // GET /communication/calls
  app.get('/communication/calls', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const query = HistoryQuerySchema.parse(request.query);
    const [items, totalCount] = await Promise.all([
      container.calls.listRecentForUser({
        tenantId: principal.tenantId,
        userId: principal.userId,
        take: query.size,
        skip: (query.page - 1) * query.size,
      }),
      container.calls.countRecentForUser(principal.tenantId, principal.userId),
    ]);
    return reply.send({
      items: items.map((snapshot) => ({
        id: snapshot.id,
        kind: snapshot.kind,
        status: snapshot.status,
        callerUserId: snapshot.callerUserId,
        calleeUserId: snapshot.calleeUserId,
        conversationId: snapshot.conversationId,
        ringingAtUtc: snapshot.ringingAtUtc.toISOString(),
        endedAtUtc: snapshot.endedAtUtc ? snapshot.endedAtUtc.toISOString() : null,
        durationSeconds: snapshot.durationSeconds,
        recordingFileId: snapshot.recordingFileId,
        endReason: snapshot.endReason,
      })),
      page: query.page,
      size: query.size,
      totalCount,
    });
  });

  // GET /communication/customers/:customerId/calls
  // Historial de llamadas IN-APP de un cliente concreto (perfil de cliente → Activity → Call history).
  // Puente: la Call no tiene CustomerId; sus participantes son UserIds de Auth. Resolvemos el UserId del
  // PORTAL del cliente (proyección CustomerPortalAccount) y listamos las llamadas donde ese usuario
  // participó = las llamadas del cliente con la oficina. Tenant-scoped por el principal.
  app.get('/communication/customers/:customerId/calls', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = CustomerCallsParamsSchema.parse(request.params);
    const query = CustomerCallsQuerySchema.parse(request.query);

    const portalAccount = await container.customerPortalAccounts.findActiveByCustomerId(params.customerId);
    // Sin cuenta de portal (o de otro tenant): el cliente no tiene identidad in-app → no puede haber llamadas.
    if (!portalAccount || portalAccount.tenantId !== principal.tenantId) {
      return reply.send({ items: [], stats: { total: 0, completed: 0, missed: 0, avgDurationSeconds: null }, hasPortalAccount: false });
    }

    const clientUserId = portalAccount.userId;
    const snapshots = await container.calls.listRecentForUser({
      tenantId: principal.tenantId,
      userId: clientUserId,
      take: query.size,
      skip: 0,
    });

    const items = snapshots.map((s) => ({
      id: s.id,
      kind: s.kind,
      status: s.status,
      // Dirección relativa a la OFICINA: si el cliente inició, es entrante; si no, saliente.
      direction: s.callerUserId === clientUserId ? 'incoming' : 'outgoing',
      conversationId: s.conversationId,
      ringingAtUtc: s.ringingAtUtc.toISOString(),
      endedAtUtc: s.endedAtUtc ? s.endedAtUtc.toISOString() : null,
      durationSeconds: s.durationSeconds,
      recordingFileId: s.recordingFileId,
      endReason: s.endReason,
    }));

    const completedCalls = snapshots.filter((s) => s.status === 'Ended');
    const completedWithDuration = completedCalls.filter((s) => (s.durationSeconds ?? 0) > 0);
    const avgDurationSeconds =
      completedWithDuration.length > 0
        ? Math.round(
            completedWithDuration.reduce((sum, s) => sum + (s.durationSeconds ?? 0), 0) / completedWithDuration.length,
          )
        : null;

    return reply.send({
      items,
      stats: {
        total: snapshots.length,
        completed: completedCalls.length,
        missed: snapshots.filter((s) => s.status === 'MissedCall').length,
        avgDurationSeconds,
      },
      hasPortalAccount: true,
      // UserId del portal del cliente — permite iniciar una llamada (audio/video) al cliente desde su perfil.
      clientUserId,
    });
  });
}
