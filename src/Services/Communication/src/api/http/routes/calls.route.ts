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
}
