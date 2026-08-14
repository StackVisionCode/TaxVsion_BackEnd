import { randomUUID } from 'node:crypto';
import { scheduleMeeting, type ScheduleMeetingDeps } from '../use-cases/schedule-meeting.js';
import { rescheduleMeeting, type RescheduleMeetingDeps } from '../use-cases/reschedule-meeting.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
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
} & Omit<ScheduleMeetingDeps, 'meetings' | 'publisher'> &
  Omit<RescheduleMeetingDeps, 'meetings' | 'publisher'>;

export function bindCalendarConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: CalendarConsumerDeps,
): void {
  register('calendar.appointment_scheduled.v1', (env) => appointmentScheduledHandler(env, deps));
  register('calendar.appointment_rescheduled.v1', (env) => appointmentRescheduledHandler(env, deps));
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

  const scheduled = await scheduleMeeting(
    {
      tenantId,
      correlationId,
      host: { userId: organizerUserId, displayName: 'Organizador' },
      title,
      scheduledForUtc: startUtc ?? null,
    },
    deps,
  );

  if (!scheduled.isSuccess) return;

  // El codigo corto vuelve a la cita por evento: es lo unico que Calendar necesita para mostrar el
  // link, y va aparte del `meeting.scheduled` generico porque lleva el id de la cita.
  const linked: MeetingLinkedToAppointmentEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.LinkedToAppointment,
    tenantId,
    correlationId,
    occurredOnUtc: new Date().toISOString(),
    appointmentId,
    meetingId: scheduled.value.meetingId,
    shortCode: scheduled.value.shortCode,
    scheduledForUtc: startUtc ?? null,
  };

  await deps.publisher.enqueue(linked);
}

/**
 * La cita se movio: la sala la sigue.
 *
 * El `meetingId` viaja en el evento en vez de buscarse por el id de la cita. Calendar ya lo guarda
 * desde que la sala se creo, asi que la alternativa —una columna `AppointmentId` en Meeting— seria
 * duplicar el vinculo en los dos lados para no leerlo de donde ya esta.
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
    },
    deps,
  );

  // Tragarse el fallo deja la sala en la hora vieja sin que nada lo diga: el reintento de la cola es
  // lo unico que puede arreglarlo, y solo se dispara si esto lanza.
  if (!moved.isSuccess) {
    throw new Error(`No se pudo mover la sala ${meetingId}: ${moved.error.code}`);
  }
}
