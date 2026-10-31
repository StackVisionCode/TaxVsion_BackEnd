import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import {
  checkPermission,
  permissionCheckHttpStatus,
  CommunicationPermissions,
  isStaffActor,
} from '../../../domain/shared/permissions.js';
import { seesAllCustomers } from '../../../application/use-cases/customer-visibility.js';
import { scheduleMeeting } from '../../../application/use-cases/schedule-meeting.js';
import { startMeeting, endMeeting } from '../../../application/use-cases/meeting-lifecycle.js';
import { cancelMeeting } from '../../../application/use-cases/cancel-meeting.js';
import { rescheduleMeeting } from '../../../application/use-cases/reschedule-meeting.js';
import { continueMeeting } from '../../../application/use-cases/continue-meeting.js';
import { listMeetingEvents } from '../../../application/use-cases/meeting-events-query.js';
import { getMeetingSummary } from '../../../application/use-cases/get-meeting-summary.js';
import { getMeetingChatHistory } from '../../../application/use-cases/get-meeting-chat-history.js';
import { resolveDisplayName } from '../../socket/handlers/resolve-display-name.js';
import { requirePermission } from '../plugins/require-permission.js';
import { RecordingScope } from '../../../domain/recording/recording-session-state.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const CreateMeetingBody = z.object({
  title: z.string().min(1).max(200),
  description: z.string().max(1000).optional(),
  maxParticipants: z.number().int().min(2).max(100).optional(),
  passcode: z.string().min(4).max(120).optional(),
  requireWaitingRoom: z.boolean().optional(),
  scheduledForUtc: z.string().datetime().optional(),
  // Informativa (D-35): el dominio la acota al máximo de la oficina.
  scheduledDurationMinutes: z.number().int().min(1).max(480).optional(),
  recordingRequested: z.boolean().optional(),
});

const IdParams = z.object({ id: z.string().uuid() });
const StartMeetingBody = z.object({
  audioDefault: z.boolean().optional(),
  videoDefault: z.boolean().optional(),
});
const CancelMeetingBody = z.object({ reason: z.string().max(500).optional() });
const RescheduleMeetingBody = z.object({ newScheduledForUtc: z.string().datetime().nullable() });
const EventsQuery = z.object({
  after: z.coerce.number().int().min(0).optional(),
  take: z.coerce.number().int().min(1).max(500).default(200),
  // Ficha: solo la actividad legible (sin voz, calidad ni media).
  activity: z.enum(['true', 'false']).optional(),
});
const MeetingChatQuery = z.object({
  before: z.string().datetime().optional(),
  take: z.coerce.number().int().min(1).max(100).default(50),
});
const HistoryQuery = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
  // Fase Frontend 9 — "historial" (meetings ya terminadas/canceladas, para
  // ver su transcript) no existia como concepto: listUpcomingForUser filtra
  // explicitamente Status IN (Scheduled, Live), asi que un meeting Ended
  // nunca aparecia en ningun listado. Default 'upcoming' preserva el
  // comportamiento previo para callers existentes.
  scope: z.enum(['upcoming', 'past']).default('upcoming'),
  // Perfil del cliente: solo meetings donde ese cliente participa (columna CustomerId, D-58) o su
  // usuario del portal está invitado. Solo staff, y con la misma visibilidad por asignación que el
  // historial de llamadas.
  customerId: z.string().uuid().optional(),
});

const CUSTOMER_MEETINGS_STAFF_ONLY = {
  code: 'Auth.Forbidden',
  message: 'Client meeting history is staff-only.',
} as const;
const UserIdParams = z.object({ userId: z.string().uuid() });

export async function registerMeetingRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.post('/communication/meetings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const permCheck = await checkPermission(
      principal,
      CommunicationPermissions.MeetingCreate,
      container.userPermissions,
    );
    if (!permCheck.allowed) {
      return reply
        .code(permissionCheckHttpStatus(permCheck))
        .send({ code: permCheck.code, message: permCheck.message });
    }
    const body = CreateMeetingBody.parse(request.body);
    // Nombre real del host (directorio, con fallback al email del JWT) — sin esto el participante del
    // host quedaba con displayName = su GUID y la tile del meeting mostraba el UUID crudo.
    const hostEmail = typeof principal.raw['email'] === 'string' ? principal.raw['email'] : undefined;
    const hostDisplayName = await resolveDisplayName(container.userDirectory, principal.userId, hostEmail);
    const result = await scheduleMeeting(
      {
        tenantId: principal.tenantId,
        correlationId: request.id,
        host: { userId: principal.userId, displayName: hostDisplayName },
        title: body.title,
        description: body.description ?? null,
        ...(body.maxParticipants !== undefined ? { maxParticipants: body.maxParticipants } : {}),
        passcodePlain: body.passcode ?? null,
        ...(body.requireWaitingRoom !== undefined ? { requireWaitingRoom: body.requireWaitingRoom } : {}),
        ...(body.scheduledForUtc !== undefined ? { scheduledForUtc: body.scheduledForUtc } : {}),
        ...(body.scheduledDurationMinutes !== undefined ? { scheduledDurationMinutes: body.scheduledDurationMinutes } : {}),
        ...(body.recordingRequested !== undefined ? { recordingRequested: body.recordingRequested } : {}),
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.code(201).send(result.value);
  });

  app.get(
    '/communication/meetings',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingJoin)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const query = HistoryQuery.parse(request.query);

      let counterpartUserId: string | undefined;
      if (query.customerId) {
        if (!isStaffActor(principal.actorType)) return reply.code(403).send(CUSTOMER_MEETINGS_STAFF_ONLY);

        const settings = await container.settings.get(principal.tenantId);
        const restrictToAssigned =
          container.assignmentVisibilityEnabled || settings.restrictCustomerChatToAssignedPreparer;
        if (restrictToAssigned && !(await seesAllCustomers(principal, container.userPermissions))) {
          const isAssigned = await container.customerAssignments.isAssigned(
            principal.tenantId,
            query.customerId,
            principal.userId,
          );
          if (!isAssigned) return reply.code(403).send(CUSTOMER_MEETINGS_STAFF_ONLY);
        }

        // D-58: la columna CustomerId del participante es la fuente; el usuario de portal activo solo
        // suma invitaciones sin usar. Sin cuenta de portal el historial sigue existiendo (E-199).
        const portalAccount = await container.customerPortalAccounts.findActiveByCustomerId(query.customerId);
        if (portalAccount && portalAccount.tenantId === principal.tenantId) {
          counterpartUserId = portalAccount.userId;
        }
      }

      const listInput = {
        tenantId: principal.tenantId,
        userId: principal.userId,
        ...(query.customerId !== undefined ? { customerId: query.customerId } : {}),
        ...(counterpartUserId !== undefined ? { counterpartUserId } : {}),
      };
      const pageInput = { ...listInput, take: query.size, skip: (query.page - 1) * query.size };
      const [items, totalCount] =
        query.scope === 'past'
          ? await Promise.all([container.meetings.listPastForUser(pageInput), container.meetings.countPastForUser(listInput)])
          : await Promise.all([
              container.meetings.listUpcomingForUser(pageInput),
              container.meetings.countUpcomingForUser(listInput),
            ]);
      // F7: estado de la grabacion por item; el portal solo lo ve si la oficina hace visibles las grabaciones.
      const portalHidesRecordings =
        principal.actorType === 'CustomerPortal' &&
        !(await container.effectiveSettings.resolve(principal.tenantId)).recording.visibleInPortal;
      const recordingStates = portalHidesRecordings
        ? new Map<string, string>()
        : await container.recordingSessions.listStatesByScopeIds(principal.tenantId, RecordingScope.Meeting, items.map((m) => m.id));
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
          // F3 (T-40): duración programada (informativa), tope, corte y motivo del fin.
          scheduledDurationMinutes: snapshot.scheduledDurationMinutes,
          maxDurationMinutes: snapshot.maxDurationMinutes,
          endsAtUtc: snapshot.endsAtUtc?.toISOString() ?? null,
          endReason: snapshot.endReason,
          durationSeconds: snapshot.durationSeconds,
          continuationOfMeetingId: snapshot.continuationOfMeetingId,
          // En vivo: quienes están ahora; terminada: quienes llegaron a entrar (todos quedan en Left).
          joinedParticipantsCount: snapshot.participants.filter((p) =>
            snapshot.status === 'Live' ? p.status === 'Joined' : p.joinedAtUtc !== null,
          ).length,
          // Fase Frontend 9 — el historial necesita saber si hay transcript
          // disponible sin abrir cada meeting; el campo ya vivia en el
          // aggregate/Prisma (Fase Transcript 5/6), solo faltaba exponerlo aca.
          transcriptFileId: snapshot.transcriptFileId,
          recordingState: recordingStates.get(snapshot.id) ?? null,
        })),
        page: query.page,
        size: query.size,
        totalCount,
      });
    },
  );

  app.get(
    '/communication/meetings/stats',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingJoin)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const tz = typeof principal.raw['zoneinfo'] === 'string' ? principal.raw['zoneinfo'] : 'UTC';
      const now = new Date();
      const { dayStartUtc, dayEndUtc, weekEndUtc } = userDayWindow(now, tz);
      const stats = await container.meetings.getStatsForUser({
        tenantId: principal.tenantId,
        userId: principal.userId,
        nowUtc: now,
        dayStartUtc,
        dayEndUtc,
        weekEndUtc,
      });
      return reply.send(stats);
    },
  );

  // Pre-flight de impacto (punto 3.2): cuántas reuniones activas tiene como host el empleado a retirar.
  app.get(
    '/communication/offboarding-impact/:userId',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const params = UserIdParams.parse(request.params);
      const activeMeetings = await container.meetings.countActiveHostedBy(
        request.principal!.tenantId,
        params.userId,
      );
      return reply.send({ activeMeetings });
    },
  );

  // F3: limites efectivos de la oficina para agendar (duracion por defecto/maxima, cupo, invitados).
  // Cualquier miembro con meeting.join los lee; los valores salen del resolver (plataforma > plan > oficina).
  app.get(
    '/communication/meetings/limits',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingJoin)],
    },
    async (request, reply) => {
      const effective = await container.effectiveSettings.resolve(request.principal!.tenantId);
      return reply.send({
        maxDurationMinutes: effective.meeting.maxDurationMinutes,
        defaultDurationMinutes: effective.meeting.defaultDurationMinutes,
        maxParticipants: effective.meeting.maxParticipants,
        guestsAllowed: effective.meeting.guestsAllowed,
      });
    },
  );

  // F3 (T-32): ficha del meeting — quien participó lee su MeetingEventLog, paginado por `seq`.
  app.get(
    '/communication/meetings/:id/events',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingJoin)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const params = IdParams.parse(request.params);
      const query = EventsQuery.parse(request.query);
      const result = await listMeetingEvents(
        {
          tenantId: principal.tenantId,
          meetingId: params.id,
          userId: principal.userId,
          ...(query.after !== undefined ? { afterSeq: query.after } : {}),
          activityOnly: query.activity === 'true',
          take: query.take,
        },
        container,
      );
      if (!result.isSuccess) {
        const status = result.error.code === 'Meeting.NotFound' ? 404 : 403;
        return reply.code(status).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // Ficha del meeting: resumen, asistencia por persona (desde el log), invitados que no entraron y totales.
  app.get(
    '/communication/meetings/:id/summary',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingJoin)],
    },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const params = IdParams.parse(request.params);
      const isPortal = principal.actorType === 'CustomerPortal';
      const recordingVisible =
        !isPortal || (await container.effectiveSettings.resolve(principal.tenantId)).recording.visibleInPortal;
      const result = await getMeetingSummary(
        { tenantId: principal.tenantId, meetingId: params.id, userId: principal.userId, isPortal, recordingVisible },
        container,
      );
      if (!result.isSuccess) {
        const status = result.error.code === 'Meeting.NotFound' ? 404 : 403;
        return reply.code(status).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // F8a (D-01): chat de la sala en la ficha del meeting (solo lectura tras el End), para quien participó.
  app.get(
    '/communication/meetings/:id/chat',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingJoin)],
    },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const params = IdParams.parse(request.params);
      const query = MeetingChatQuery.parse(request.query);
      const result = await getMeetingChatHistory(
        {
          tenantId: principal.tenantId,
          meetingId: params.id,
          userId: principal.userId,
          ...(query.before !== undefined ? { beforeUtc: query.before } : {}),
          take: query.take,
        },
        container,
      );
      if (!result.isSuccess) {
        const status = result.error.code === 'Meeting.NotFound' ? 404 : 403;
        return reply.code(status).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  app.post(
    '/communication/meetings/:id/start',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingHost)],
    },
    async (request, reply) => {
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
        { ...container, emitter: container.emitter },
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  app.post(
    '/communication/meetings/:id/cancel',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingHost)],
    },
    async (request, reply) => {
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
    },
  );

  app.post(
    '/communication/meetings/:id/reschedule',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingHost)],
    },
    async (request, reply) => {
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
    },
  );

  // End explícito de host/cohost (D-07): el cierre ordenado (grabación, SFU, "busy", avisos) es el
  // mismo que usan el scheduler y la sucesión; el endpoint ya no lo repite a mano.
  app.post(
    '/communication/meetings/:id/end',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingHost)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const params = IdParams.parse(request.params);
      const result = await endMeeting(
        {
          tenantId: principal.tenantId,
          correlationId: request.id,
          meetingId: params.id,
          byUserId: principal.userId,
        },
        { ...container, emitter: container.emitter },
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // "Start a new meeting" (D-34): sin prórroga, la continuación es una reunión nueva enlazada con
  // los mismos invitados; cada uno recibe `meeting.invited` y entra con un clic.
  app.post(
    '/communication/meetings/:id/continue',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.MeetingCreate)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const params = IdParams.parse(request.params);
      const hostEmail = typeof principal.raw['email'] === 'string' ? principal.raw['email'] : undefined;
      const actorDisplayName = await resolveDisplayName(container.userDirectory, principal.userId, hostEmail);
      const result = await continueMeeting(
        {
          tenantId: principal.tenantId,
          correlationId: request.id,
          meetingId: params.id,
          actorUserId: principal.userId,
          actorDisplayName,
        },
        { ...container, ...(container.emitter ? { emitter: container.emitter } : {}) },
      );
      if (!result.isSuccess) {
        const status = result.error.code === 'Meeting.NotFound' ? 404 : 400;
        return reply.code(status).send({ code: result.error.code, message: result.error.message });
      }
      return reply.code(201).send(result.value);
    },
  );
}

/**
 * Fronteras de "hoy" y "esta semana" en la zona horaria del usuario, resueltas a instantes UTC (los
 * meetings se guardan en UTC). Sin depender de una librería: se calcula el offset de la tz en `now` con
 * Intl y se derivan la medianoche local y +7 días. Si la tz es inválida, cae a UTC.
 */
function userDayWindow(
  now: Date,
  timeZone: string,
): { dayStartUtc: Date; dayEndUtc: Date; weekEndUtc: Date } {
  const offsetMs = tzOffsetMs(now, timeZone);
  // "now" visto en hora local, como si fueran componentes UTC.
  const local = new Date(now.getTime() + offsetMs);
  const localMidnight = Date.UTC(local.getUTCFullYear(), local.getUTCMonth(), local.getUTCDate());
  const dayStartUtc = new Date(localMidnight - offsetMs);
  const dayEndUtc = new Date(dayStartUtc.getTime() + 24 * 60 * 60 * 1000);
  const weekEndUtc = new Date(dayStartUtc.getTime() + 7 * 24 * 60 * 60 * 1000);
  return { dayStartUtc, dayEndUtc, weekEndUtc };
}

/** Offset (local - UTC) en ms de una zona horaria IANA en un instante dado. UTC si la tz es inválida. */
function tzOffsetMs(date: Date, timeZone: string): number {
  try {
    const dtf = new Intl.DateTimeFormat('en-US', {
      timeZone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
    const parts = dtf.formatToParts(date);
    const map: Record<string, number> = {};
    for (const p of parts) {
      if (p.type !== 'literal') map[p.type] = Number(p.value);
    }
    const asUtc = Date.UTC(
      map['year']!,
      map['month']! - 1,
      map['day']!,
      map['hour']!,
      map['minute']!,
      map['second']!,
    );
    return asUtc - date.getTime();
  } catch {
    return 0;
  }
}
