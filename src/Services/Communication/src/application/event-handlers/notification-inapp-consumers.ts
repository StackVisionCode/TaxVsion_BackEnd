import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { InAppNotificationRepository } from '../ports/in-app-notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { toNotificationDto } from '../use-cases/notification-dto.js';
import { Notification } from '../../domain/notifications/notification.js';
import { NotificationSocketEvents } from '../../contracts/socket/notification-socket-events.js';
import { readDotNetField } from '../../contracts/events/dotnet-payload.js';

// Trunca textos largos sin rechazar el payload: tirar un titulo de 201 caracteres
// a DLQ pierde la notificacion, truncarlo preserva el mensaje y respeta el ancho
// de la columna. Usar siempre despues de .trim() en el schema.
const truncated = (max: number) =>
  z
    .string()
    .trim()
    .transform((value) => (value.length > max ? value.slice(0, max) : value));

const InAppNotificationPayloadSchema = z.object({
  recipientUserId: z.string().uuid(),
  category: z.enum([
    'Security',
    'Work',
    'Clients',
    'Signatures',
    'CalendarMeetings',
    'DocumentsStorage',
    'Billing',
    'Portal',
  ]),
  kind: truncated(64).refine((value) => value.length > 0, { message: 'kind required' }),
  priority: z.enum(['Low', 'Normal', 'High', 'Urgent']),
  titleKey: truncated(160).refine((value) => value.length > 0, { message: 'titleKey required' }),
  bodyKey: truncated(160).nullable().optional(),
  params: z.record(z.string(), z.string()).default({}),
  fallbackTitle: truncated(200).refine((value) => value.length > 0, { message: 'fallbackTitle required' }),
  // Vacio tras trim equivale a ausente — el consumer caera a fallbackTitle.
  fallbackBody: truncated(1000)
    .transform((value) => (value.length === 0 ? null : value))
    .nullable()
    .optional(),
  actorUserId: z.string().uuid().nullable().optional(),
  actorDisplayName: truncated(160).nullable().optional(),
  resourceType: truncated(64).refine((value) => value.length > 0, { message: 'resourceType required' }),
  resourceId: truncated(128).nullable().optional(),
  resourceContext: z.record(z.string(), z.string()).default({}),
  action: z.enum(['open', 'review', 'sign', 'pay', 'reconnect']),
  sourceEventId: z.string().uuid(),
  sourceEventType: truncated(128).refine((value) => value.length > 0, {
    message: 'sourceEventType required',
  }),
  groupKey: truncated(256).nullable().optional(),
  occurredAtUtc: z.string().datetime({ offset: true }),
});

export function parseInAppNotificationRequested(payload: Record<string, unknown>) {
  return InAppNotificationPayloadSchema.safeParse({
    recipientUserId: readDotNetField(payload, 'RecipientUserId', 'recipientUserId'),
    category: readDotNetField(payload, 'Category', 'category'),
    kind: readDotNetField(payload, 'Kind', 'kind'),
    priority: readDotNetField(payload, 'Priority', 'priority'),
    titleKey: readDotNetField(payload, 'TitleKey', 'titleKey'),
    bodyKey: readDotNetField(payload, 'BodyKey', 'bodyKey'),
    params: readDotNetField(payload, 'Params', 'params'),
    fallbackTitle: readDotNetField(payload, 'FallbackTitle', 'fallbackTitle'),
    fallbackBody: readDotNetField(payload, 'FallbackBody', 'fallbackBody'),
    actorUserId: readDotNetField(payload, 'ActorUserId', 'actorUserId'),
    actorDisplayName: readDotNetField(payload, 'ActorDisplayName', 'actorDisplayName'),
    resourceType: readDotNetField(payload, 'ResourceType', 'resourceType'),
    resourceId: readDotNetField(payload, 'ResourceId', 'resourceId'),
    resourceContext: readDotNetField(payload, 'ResourceContext', 'resourceContext'),
    action: readDotNetField(payload, 'Action', 'action'),
    sourceEventId: readDotNetField(payload, 'SourceEventId', 'sourceEventId'),
    sourceEventType: readDotNetField(payload, 'SourceEventType', 'sourceEventType'),
    groupKey: readDotNetField(payload, 'GroupKey', 'groupKey'),
    occurredAtUtc: readDotNetField(payload, 'OccurredAtUtc', 'occurredAtUtc'),
  });
}

export function bindInAppNotificationConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { notifications: InAppNotificationRepository; emitter: RealtimeEmitter },
): void {
  register('notification.inapp.requested.v1', async (env) => {
    const parsed = parseInAppNotificationRequested(env.payload);
    if (!parsed.success) throw new Error(`Invalid in-app notification payload: ${parsed.error.message}`);

    const payload = parsed.data;
    const occurredAtUtc = new Date(payload.occurredAtUtc);
    // CreatedAtUtc = hora de llegada al servidor; OccurredAtUtc guarda el hecho
    // original. Separar ambos evita que un evento con fecha futura (ej. cita que
    // empieza manana) quede clavado arriba o fuera del corte de read-all.
    const createdAtUtc = new Date();
    // La columna CorrelationId es NVarChar(64); un id mas largo rompe el INSERT.
    const correlationId = env.correlationId ? env.correlationId.slice(0, 64) : null;
    const effectiveBody = payload.fallbackBody ?? payload.fallbackTitle;
    const creation = Notification.create({
      tenantId: env.tenantId,
      userId: payload.recipientUserId,
      kind: payload.kind,
      priority: payload.priority,
      title: payload.fallbackTitle,
      body: effectiveBody,
      metadata: {
        resourceType: payload.resourceType,
        resourceId: payload.resourceId ?? null,
        action: payload.action,
        ...payload.resourceContext,
      },
      category: payload.category,
      actorUserId: payload.actorUserId ?? null,
      actorDisplayName: payload.actorDisplayName ?? null,
      resourceType: payload.resourceType,
      resourceId: payload.resourceId ?? null,
      resourceContext: payload.resourceContext,
      titleKey: payload.titleKey,
      bodyKey: payload.bodyKey ?? null,
      params: payload.params,
      action: payload.action,
      groupKey: payload.groupKey ?? null,
      occurredAtUtc,
      sourceEventId: payload.sourceEventId,
      sourceEventType: payload.sourceEventType,
      correlationId,
      now: createdAtUtc,
    });
    if (!creation.isSuccess) throw new Error(creation.error.message);

    const persisted = await deps.notifications.persist(creation.value);
    if (persisted.status === 'duplicate' || !persisted.notification) return;

    const asOfUtc = new Date().toISOString();
    const unreadCount = await deps.notifications.countUnread(env.tenantId, payload.recipientUserId);
    const envelope = {
      eventId: randomUUID(),
      correlationId: env.correlationId ?? '',
      emittedAtUtc: asOfUtc,
      payload: toNotificationDto(persisted.notification),
    };
    deps.emitter.emitToUser({
      tenantId: env.tenantId,
      userId: payload.recipientUserId,
      event:
        persisted.status === 'updated' ? NotificationSocketEvents.Updated : NotificationSocketEvents.Received,
      envelope,
    });
    deps.emitter.emitToUser({
      tenantId: env.tenantId,
      userId: payload.recipientUserId,
      event: NotificationSocketEvents.UnreadCountChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: asOfUtc,
        payload: { count: unreadCount, asOfUtc },
      },
    });
  });
}
