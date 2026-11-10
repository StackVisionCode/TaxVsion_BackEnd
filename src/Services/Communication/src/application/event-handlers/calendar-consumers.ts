import { randomUUID } from 'node:crypto';
import { scheduleMeeting, type ScheduleMeetingDeps } from '../use-cases/schedule-meeting.js';
import { rescheduleMeeting, type RescheduleMeetingDeps } from '../use-cases/reschedule-meeting.js';
import { cancelMeeting, type CancelMeetingDeps } from '../use-cases/cancel-meeting.js';
import {
  createMeetingInvitations,
  type CreateMeetingInvitationInput,
  type CreateMeetingInvitationsDeps,
} from '../use-cases/create-meeting-invitations.js';
import { logger } from '../../infrastructure/logger/logger.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { AppointmentRoomLock } from '../ports/appointment-room-lock.js';
import {
  MeetingEventTypes,
  type MeetingLinkedToAppointmentEvent,
} from '../../contracts/events/meeting-events.js';

/**
 * Calendar es dueno del compromiso —cuando, quien asiste, si choca— y Communication de la sala.
 * Cuando se agenda una cita virtual, aca nace su meeting y el codigo corto vuelve por evento.
 *
 * Calendar NUNCA llama por HTTP: si Communication esta caido, la cita se crea igual y la sala aparece
 * cuando el servicio vuelva, porque el mensaje espera en la cola.
 *
 * `ScheduledForUtc` del meeting es una copia denormalizada para pintar la sala. Si diverge de la cita,
 * manda la cita: es la fuente de verdad de la hora.
 */
type CalendarConsumerDeps = {
  meetings: MeetingRepository;
  publisher: IntegrationEventPublisher;
  appointmentRoomLock: AppointmentRoomLock;
} & Omit<ScheduleMeetingDeps, 'meetings' | 'publisher'> &
  Omit<RescheduleMeetingDeps, 'meetings' | 'publisher'> &
  Omit<CancelMeetingDeps, 'meetings' | 'publisher'> &
  Omit<CreateMeetingInvitationsDeps, 'meetings' | 'publisher'>;

export function bindCalendarConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: CalendarConsumerDeps,
): void {
  register('calendar.appointment_scheduled.v1', (env) => appointmentScheduledHandler(env, deps));
  register('calendar.appointment_rescheduled.v1', (env) => appointmentRescheduledHandler(env, deps));
  register('calendar.appointment_cancelled.v1', (env) => appointmentCancelledHandler(env, deps));
  register('calendar.attendee_added.v1', (env) => attendeeAddedHandler(env, deps));
  register('calendar.appointment_details_changed.v1', (env) => detailsChangedHandler(env, deps));
  // Misma sala, otro disparador: el job de Calendar reclama la que nunca llego. El handler ya ignora
  // lo que no es virtual, y este evento solo se emite para citas virtuales.
  register('calendar.appointment_meeting_room_requested.v1', (env) =>
    appointmentScheduledHandler({ ...env, payload: { ...(env.payload as object), IsVirtual: true } }, deps),
  );
}

type ScheduledPayload = {
  appointmentId?: string;
  AppointmentId?: string;
  tenantId?: string;
  TenantId?: string;
  title?: string;
  Title?: string;
  organizerUserId?: string;
  OrganizerUserId?: string;
  startUtc?: string;
  StartUtc?: string;
  endUtc?: string;
  EndUtc?: string;
  isVirtual?: boolean;
  IsVirtual?: boolean;
  correlationId?: string;
  CorrelationId?: string;
};

/**
 * Wolverine serializa con las propiedades en PascalCase y el outbox propio en camelCase, asi que cada
 * campo se lee en las dos formas. Sin esto el handler no falla: lee `undefined` y crea una sala sin
 * titulo ni hora, que es peor que fallar.
 */
function read(payload: ScheduledPayload, camel: keyof ScheduledPayload, pascal: keyof ScheduledPayload): unknown {
  return payload[camel] ?? payload[pascal];
}

async function appointmentScheduledHandler(env: IncomingEnvelope, deps: CalendarConsumerDeps): Promise<void> {
  const payload = env.payload as ScheduledPayload;

  const isVirtual = read(payload, 'isVirtual', 'IsVirtual') === true;
  if (!isVirtual) return;

  const tenantId = read(payload, 'tenantId', 'TenantId') as string | undefined;
  const appointmentId = read(payload, 'appointmentId', 'AppointmentId') as string | undefined;
  const organizerUserId = read(payload, 'organizerUserId', 'OrganizerUserId') as string | undefined;
  const title = read(payload, 'title', 'Title') as string | undefined;
  if (!tenantId || !appointmentId || !organizerUserId || !title) return;

  const correlationId = (read(payload, 'correlationId', 'CorrelationId') as string | undefined) ?? randomUUID();
  const startUtc = read(payload, 'startUtc', 'StartUtc') as string | undefined;
  const endUtc = read(payload, 'endUtc', 'EndUtc') as string | undefined;
  // La sala dura lo que la cita (sin esto quedaba con la duración por defecto de la oficina).
  const durationMinutes =
    startUtc && endUtc ? Math.round((Date.parse(endUtc) - Date.parse(startUtc)) / 60_000) : undefined;

  // Una sala por cita: el aviso original y la reparación del job pueden llegar juntos. Con la sala ya
  // creada solo se vuelve a ligar (Calendar la guarda de nuevo, idempotente).
  const room = await deps.appointmentRoomLock.run(tenantId, appointmentId, async () => {
    const existing = await deps.meetings.findByAppointmentId(tenantId, appointmentId);
    if (existing) {
      const snapshot = existing.toSnapshot();
      return { meetingId: snapshot.id, shortCode: snapshot.shortCode, created: false };
    }
    const scheduled = await scheduleMeeting(
      {
        tenantId,
        correlationId,
        host: { userId: organizerUserId, displayName: 'Organizador' },
        title,
        scheduledForUtc: startUtc ?? null,
        appointmentId,
        ...(durationMinutes && durationMinutes > 0 ? { scheduledDurationMinutes: durationMinutes } : {}),
      },
      deps,
    );
    return scheduled.isSuccess ? { ...scheduled.value, created: true } : null;
  });

  if (!room) return;
  if (room.created) {
    await inviteAttendees(payload, { tenantId, correlationId, meetingId: room.meetingId, organizerUserId }, deps);
  }

  // El codigo corto vuelve a la cita por evento: es lo unico que Calendar necesita para mostrar el
  // link, y va aparte del `meeting.scheduled` generico porque lleva el id de la cita.
  const linked: MeetingLinkedToAppointmentEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.LinkedToAppointment,
    tenantId,
    correlationId,
    occurredOnUtc: new Date().toISOString(),
    appointmentId,
    meetingId: room.meetingId,
    shortCode: room.shortCode,
    scheduledForUtc: startUtc ?? null,
  };

  await deps.publisher.enqueue(linked);
}

type RecipientPayload = {
  email?: string;
  Email?: string;
  userId?: string | null;
  UserId?: string | null;
  customerId?: string | null;
  CustomerId?: string | null;
  displayName?: string | null;
  DisplayName?: string | null;
  rsvpToken?: string | null;
  RsvpToken?: string | null;
};

/**
 * Los asistentes de la cita quedan invitados a su sala: el personal por su usuario y el resto por email,
 * que `createMeetingInvitations` reconoce como cliente (y resuelve su usuario del portal) si lo es. Uno
 * por uno: un externo que la oficina no admite no deja sin invitación a los demás. Nunca corta la cita.
 */
async function inviteAttendees(
  payload: ScheduledPayload & { recipients?: RecipientPayload[]; Recipients?: RecipientPayload[] },
  room: { tenantId: string; correlationId: string; meetingId: string; organizerUserId: string },
  deps: CalendarConsumerDeps,
): Promise<void> {
  const recipients = payload.recipients ?? payload.Recipients ?? [];
  for (const recipient of recipients) {
    const email = recipient.email ?? recipient.Email;
    const userId = recipient.userId ?? recipient.UserId ?? null;
    const customerId = recipient.customerId ?? recipient.CustomerId ?? null;
    const name = recipient.displayName ?? recipient.DisplayName ?? undefined;
    const rsvpToken = recipient.rsvpToken ?? recipient.RsvpToken ?? undefined;
    if (!email || userId === room.organizerUserId) continue;
    // El cliente va como cliente: con solo el correo podría confundirse con alguien del equipo.
    const base = { email, ...(name ? { name } : {}), ...(rsvpToken ? { rsvpToken } : {}) };
    const invitee: CreateMeetingInvitationInput = userId
      ? { kind: 'Employee', userId, ...base }
      : customerId
        ? { kind: 'Customer', customerId, ...base }
        : { kind: 'External', ...base };
    try {
      const invited = await createMeetingInvitations(
        {
          tenantId: room.tenantId,
          correlationId: room.correlationId,
          meetingId: room.meetingId,
          actorUserId: room.organizerUserId,
          invitees: [invitee],
        },
        deps,
      );
      if (!invited.isSuccess) {
        logger.info({ meetingId: room.meetingId, code: invited.error.code }, 'appointment room invite skipped');
      }
    } catch (err) {
      logger.warn({ meetingId: room.meetingId, err: (err as Error).message }, 'appointment room invite failed');
    }
  }
}

/**
 * Alguien se sumó a una cita virtual ya creada: queda invitado a su sala, igual que los del alta
 * (CAL-10). Una cita presencial, o una virtual cuya sala todavía no existe, no hace nada: la sala nueva
 * invita a todos los asistentes al crearse.
 */
async function attendeeAddedHandler(env: IncomingEnvelope, deps: CalendarConsumerDeps): Promise<void> {
  const payload = env.payload as Record<string, unknown>;
  const pick = (camel: string, pascal: string): unknown => payload[camel] ?? payload[pascal];
  if (pick('isVirtual', 'IsVirtual') !== true) return;
  const tenantId = pick('tenantId', 'TenantId') as string | undefined;
  const appointmentId = pick('appointmentId', 'AppointmentId') as string | undefined;
  const email = pick('email', 'Email') as string | undefined;
  if (!tenantId || !appointmentId || !email) return;

  const meeting = await deps.meetings.findByAppointmentId(tenantId, appointmentId);
  if (!meeting) return;
  const snapshot = meeting.toSnapshot();
  if (snapshot.status !== 'Scheduled' && snapshot.status !== 'Live') return;

  const userId = (pick('userId', 'UserId') as string | null | undefined) ?? null;
  const customerId = (pick('customerId', 'CustomerId') as string | null | undefined) ?? null;
  const displayName = (pick('displayName', 'DisplayName') as string | null | undefined) ?? null;
  const rsvpToken = (pick('rsvpToken', 'RsvpToken') as string | null | undefined) ?? null;
  await inviteAttendees(
    { recipients: [{ email, userId, customerId, displayName, rsvpToken }] } as ScheduledPayload & { recipients: RecipientPayload[] },
    {
      tenantId,
      correlationId: (pick('correlationId', 'CorrelationId') as string | undefined) ?? randomUUID(),
      meetingId: snapshot.id,
      organizerUserId: snapshot.hostUserId,
    },
    deps,
  );
}

/**
 * La cita se movio: la sala la sigue.
 *
 * El `meetingId` viaja en el evento: Calendar lo guarda desde que la sala se creo.
 */
async function appointmentRescheduledHandler(env: IncomingEnvelope, deps: CalendarConsumerDeps): Promise<void> {
  const payload = env.payload as ScheduledPayload & {
    newStartUtc?: string;
    NewStartUtc?: string;
    meetingId?: string;
    MeetingId?: string;
  };

  const tenantId = read(payload, 'tenantId', 'TenantId') as string | undefined;
  const meetingId = payload.meetingId ?? payload.MeetingId;
  const newStartUtc = payload.newStartUtc ?? payload.NewStartUtc;
  if (!tenantId || !meetingId || !newStartUtc) return;

  const meeting = await deps.meetings.findById(tenantId, meetingId);
  if (!meeting) return;

  const moved = await rescheduleMeeting(
    {
      tenantId,
      correlationId: (read(payload, 'correlationId', 'CorrelationId') as string | undefined) ?? randomUUID(),
      meetingId,
      hostUserId: meeting.toSnapshot().hostUserId,
      newScheduledForUtc: newStartUtc,
      fromAppointment: true,
    },
    deps,
  );

  // Tragarse el fallo deja la sala en la hora vieja sin que nada lo diga: el reintento de la cola es
  // lo unico que puede arreglarlo, y solo se dispara si esto lanza.
  if (!moved.isSuccess) {
    throw new Error(`No se pudo mover la sala ${meetingId}: ${moved.error.code}`);
  }
}

/**
 * Cambiaron los detalles de la cita: su sala toma el nombre nuevo. Solo el título le importa; una sala
 * terminada o cancelada se deja como quedó.
 */
async function detailsChangedHandler(env: IncomingEnvelope, deps: CalendarConsumerDeps): Promise<void> {
  const payload = env.payload as Record<string, unknown>;
  const pick = (camel: string, pascal: string): unknown => payload[camel] ?? payload[pascal];
  const tenantId = pick('tenantId', 'TenantId') as string | undefined;
  const meetingId = pick('meetingId', 'MeetingId') as string | null | undefined;
  const title = pick('title', 'Title') as string | undefined;
  const changed = (pick('changedFields', 'ChangedFields') as string[] | undefined) ?? [];
  if (!tenantId || !meetingId || !title || !changed.includes('Title')) return;

  const meeting = await deps.meetings.findById(tenantId, meetingId);
  if (!meeting) return;
  const renamed = meeting.retitle({ title });
  if (!renamed.isSuccess) {
    logger.info({ meetingId, code: renamed.error.code }, 'appointment room rename skipped');
    return;
  }
  await deps.meetings.save(meeting);
}

/**
 * La cita se cancelo: su sala tambien, con el aviso de una cancelacion normal (evento + socket). Una
 * ocurrencia suelta de una serie no toca la sala, que es de la serie entera. Igual que al mover, el
 * `meetingId` viaja en el evento.
 *
 * Idempotente: solo cancela una sala `Scheduled`. Ya cancelada, terminada o vencida no hace nada, y una
 * en vivo tampoco se corta: la gente ya esta adentro.
 */
async function appointmentCancelledHandler(env: IncomingEnvelope, deps: CalendarConsumerDeps): Promise<void> {
  const payload = env.payload as ScheduledPayload & {
    scope?: string;
    Scope?: string;
    meetingId?: string;
    MeetingId?: string;
    reason?: string | null;
    Reason?: string | null;
  };

  if ((payload.scope ?? payload.Scope) === 'ThisOccurrence') return;
  const tenantId = read(payload, 'tenantId', 'TenantId') as string | undefined;
  const meetingId = payload.meetingId ?? payload.MeetingId;
  if (!tenantId || !meetingId) return;

  const meeting = await deps.meetings.findById(tenantId, meetingId);
  if (!meeting) return;
  const snapshot = meeting.toSnapshot();
  if (snapshot.status !== 'Scheduled') return;

  const reason = payload.reason ?? payload.Reason;
  const cancelled = await cancelMeeting(
    {
      tenantId,
      correlationId: (read(payload, 'correlationId', 'CorrelationId') as string | undefined) || randomUUID(),
      meetingId,
      hostUserId: snapshot.hostUserId,
      fromAppointment: true,
      ...(reason ? { reason } : {}),
    },
    deps,
  );

  // Mismo criterio que al mover: el fallo lo arregla el reintento de la cola.
  if (!cancelled.isSuccess) {
    throw new Error(`No se pudo cancelar la sala ${meetingId}: ${cancelled.error.code}`);
  }
}
