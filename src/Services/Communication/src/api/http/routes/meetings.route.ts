import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { hasPermission, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import { scheduleMeeting } from '../../../application/use-cases/schedule-meeting.js';
import { startMeeting, endMeeting } from '../../../application/use-cases/meeting-lifecycle.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const CreateMeetingBody = z.object({
  title: z.string().min(1).max(200),
  description: z.string().max(1000).optional(),
  maxParticipants: z.number().int().min(2).max(100).optional(),
  passcode: z.string().min(4).max(120).optional(),
  requireWaitingRoom: z.boolean().optional(),
  scheduledForUtc: z.string().datetime().optional(),
  recordingRequested: z.boolean().optional(),
});

const IdParams = z.object({ id: z.string().uuid() });
const HistoryQuery = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
});

export async function registerMeetingRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.post('/communication/meetings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    if (!hasPermission(principal.actorType, principal.permissions, CommunicationPermissions.MeetingCreate)) {
      return reply.code(403).send({ code: 'Auth.Forbidden', message: 'Missing communication.meeting.create.' });
    }
    const body = CreateMeetingBody.parse(request.body);
    const result = await scheduleMeeting(
      {
        tenantId: principal.tenantId,
        correlationId: request.id,
        host: { userId: principal.userId, displayName: principal.userId },
        title: body.title,
        description: body.description ?? null,
        ...(body.maxParticipants !== undefined ? { maxParticipants: body.maxParticipants } : {}),
        passcodePlain: body.passcode ?? null,
        ...(body.requireWaitingRoom !== undefined ? { requireWaitingRoom: body.requireWaitingRoom } : {}),
        ...(body.scheduledForUtc !== undefined ? { scheduledForUtc: body.scheduledForUtc } : {}),
        ...(body.recordingRequested !== undefined ? { recordingRequested: body.recordingRequested } : {}),
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.code(201).send(result.value);
  });

  app.get('/communication/meetings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const query = HistoryQuery.parse(request.query);
    const [items, totalCount] = await Promise.all([
      container.meetings.listUpcomingForUser({
        tenantId: principal.tenantId,
        userId: principal.userId,
        take: query.size,
        skip: (query.page - 1) * query.size,
      }),
      container.meetings.countUpcomingForUser(principal.tenantId, principal.userId),
    ]);
    return reply.send({
      items: items.map((snapshot) => ({
        id: snapshot.id,
        title: snapshot.title,
        status: snapshot.status,
        shortCode: snapshot.shortCode,
        strategy: snapshot.strategy,
        hostUserId: snapshot.hostUserId,
        scheduledForUtc: snapshot.scheduledForUtc?.toISOString() ?? null,
        startedAtUtc: snapshot.startedAtUtc?.toISOString() ?? null,
        endedAtUtc: snapshot.endedAtUtc?.toISOString() ?? null,
        joinedParticipantsCount: snapshot.participants.filter((p) => p.status === 'Joined').length,
      })),
      page: query.page,
      size: query.size,
      totalCount,
    });
  });

  app.post('/communication/meetings/:id/start', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const result = await startMeeting(
      {
        tenantId: principal.tenantId,
        correlationId: request.id,
        meetingId: params.id,
        hostUserId: principal.userId,
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.send(result.value);
  });

  app.post('/communication/meetings/:id/end', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const result = await endMeeting(
      {
        tenantId: principal.tenantId,
        correlationId: request.id,
        meetingId: params.id,
        byUserId: principal.userId,
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.send(result.value);
  });
}
