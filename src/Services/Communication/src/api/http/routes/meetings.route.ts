import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { hasPermission, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import { scheduleMeeting } from '../../../application/use-cases/schedule-meeting.js';
import { startMeeting, endMeeting } from '../../../application/use-cases/meeting-lifecycle.js';
import { cancelMeeting } from '../../../application/use-cases/cancel-meeting.js';
import { rescheduleMeeting } from '../../../application/use-cases/reschedule-meeting.js';
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
const StartMeetingBody = z.object({
  audioDefault: z.boolean().optional(),
  videoDefault: z.boolean().optional(),
});
const CancelMeetingBody = z.object({ reason: z.string().max(500).optional() });
const RescheduleMeetingBody = z.object({ newScheduledForUtc: z.string().datetime().nullable() });
const HistoryQuery = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
  // Fase Frontend 9 — "historial" (meetings ya terminadas/canceladas, para
  // ver su transcript) no existia como concepto: listUpcomingForUser filtra
  // explicitamente Status IN (Scheduled, Live), asi que un meeting Ended
  // nunca aparecia en ningun listado. Default 'upcoming' preserva el
  // comportamiento previo para callers existentes.
  scope: z.enum(['upcoming', 'past']).default('upcoming'),
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
    const listInput = {
      tenantId: principal.tenantId,
      userId: principal.userId,
      take: query.size,
      skip: (query.page - 1) * query.size,
    };
    const [items, totalCount] =
      query.scope === 'past'
        ? await Promise.all([
            container.meetings.listPastForUser(listInput),
            container.meetings.countPastForUser(principal.tenantId, principal.userId),
          ])
        : await Promise.all([
            container.meetings.listUpcomingForUser(listInput),
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
        // Fase Frontend 9 — el historial necesita saber si hay transcript
        // disponible sin abrir cada meeting; el campo ya vivia en el
        // aggregate/Prisma (Fase Transcript 5/6), solo faltaba exponerlo aca.
        transcriptFileId: snapshot.transcriptFileId,
      })),
      page: query.page,
      size: query.size,
      totalCount,
    });
  });

  app.post('/communication/meetings/:id/start', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const body = StartMeetingBody.parse(request.body ?? {});
    const result = await startMeeting(
      {
        tenantId: principal.tenantId,
        correlationId: request.id,
        meetingId: params.id,
        hostUserId: principal.userId,
        ...(body.audioDefault !== undefined ? { audioDefault: body.audioDefault } : {}),
        ...(body.videoDefault !== undefined ? { videoDefault: body.videoDefault } : {}),
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.send(result.value);
  });

  app.post('/communication/meetings/:id/cancel', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const body = CancelMeetingBody.parse(request.body ?? {});
    if (!container.emitter) {
      return reply.code(503).send({ code: 'Service.NotReady', message: 'Realtime emitter not wired.' });
    }
    const result = await cancelMeeting(
      {
        tenantId: principal.tenantId,
        correlationId: request.id,
        meetingId: params.id,
        hostUserId: principal.userId,
        ...(body.reason !== undefined ? { reason: body.reason } : {}),
      },
      { ...container, emitter: container.emitter },
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.code(204).send();
  });

  app.post('/communication/meetings/:id/reschedule', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const params = IdParams.parse(request.params);
    const body = RescheduleMeetingBody.parse(request.body);
    if (!container.emitter) {
      return reply.code(503).send({ code: 'Service.NotReady', message: 'Realtime emitter not wired.' });
    }
    const result = await rescheduleMeeting(
      {
        tenantId: principal.tenantId,
        correlationId: request.id,
        meetingId: params.id,
        hostUserId: principal.userId,
        newScheduledForUtc: body.newScheduledForUtc,
      },
      { ...container, emitter: container.emitter },
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.code(204).send();
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
    // Libera el router SFU (transports/producers/consumers de todos los
    // participantes) si el meeting tenia strategy 'Sfu' — no-op si nunca se
    // creo router (meetings Mesh, o Sfu que nadie llego a usar via socket).
    await container.sfu.closeMeeting(params.id);
    return reply.send(result.value);
  });
}
