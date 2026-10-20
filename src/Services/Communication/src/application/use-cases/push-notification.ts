import { randomUUID } from 'node:crypto';
import { Result } from '../../domain/shared/result.js';
import {
  Notification,
  type NotificationActorType,
  type NotificationPriority,
} from '../../domain/notifications/notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { type NotificationDto, NotificationSocketEvents } from '../../contracts/socket/notification-socket-events.js';
import { toNotificationDto } from './notification-dto.js';

/**
 * Empuja una notificacion al usuario destino. Idempotente por (tenantId,
 * sourceEventId, userId): si el consumer re-entrega el mismo evento, el
 * repo devuelve false y no re-emite el socket.
 */
export interface PushNotificationCommand {
  readonly tenantId: string;
  readonly userId: string;
  readonly kind: string;
  readonly priority?: NotificationPriority;
  readonly title: string;
  readonly body: string;
  readonly metadata?: Readonly<Record<string, unknown>>;
  readonly category?: string | null;
  readonly actorUserId?: string | null;
  readonly actorDisplayName?: string | null;
  readonly actorType?: NotificationActorType | null;
  readonly resourceType?: string | null;
  readonly resourceId?: string | null;
  readonly resourceContext?: Readonly<Record<string, string>>;
  readonly titleKey?: string | null;
  readonly bodyKey?: string | null;
  readonly params?: Readonly<Record<string, string>>;
  readonly action?: string | null;
  readonly groupKey?: string | null;
  readonly occurredAtUtc?: Date | null;
  readonly sourceEventId: string;
  readonly sourceEventType: string;
  readonly correlationId?: string | null;
}

export interface PushNotificationResult {
  readonly created: boolean;
  readonly notification: NotificationDto | null;
  readonly unreadCount: number;
  readonly asOfUtc: string;
}

export async function pushNotification(
  cmd: PushNotificationCommand,
  deps: { notifications: NotificationRepository },
): Promise<Result<PushNotificationResult>> {
  const creationResult = Notification.create({
    tenantId: cmd.tenantId,
    userId: cmd.userId,
    kind: cmd.kind,
    ...(cmd.priority !== undefined ? { priority: cmd.priority } : {}),
    title: cmd.title,
    body: cmd.body,
    ...(cmd.metadata !== undefined ? { metadata: cmd.metadata } : {}),
    ...(cmd.category !== undefined ? { category: cmd.category } : {}),
    ...(cmd.actorUserId !== undefined ? { actorUserId: cmd.actorUserId } : {}),
    ...(cmd.actorDisplayName !== undefined ? { actorDisplayName: cmd.actorDisplayName } : {}),
    ...(cmd.actorType !== undefined ? { actorType: cmd.actorType } : {}),
    ...(cmd.resourceType !== undefined ? { resourceType: cmd.resourceType } : {}),
    ...(cmd.resourceId !== undefined ? { resourceId: cmd.resourceId } : {}),
    ...(cmd.resourceContext !== undefined ? { resourceContext: cmd.resourceContext } : {}),
    ...(cmd.titleKey !== undefined ? { titleKey: cmd.titleKey } : {}),
    ...(cmd.bodyKey !== undefined ? { bodyKey: cmd.bodyKey } : {}),
    ...(cmd.params !== undefined ? { params: cmd.params } : {}),
    ...(cmd.action !== undefined ? { action: cmd.action } : {}),
    ...(cmd.groupKey !== undefined ? { groupKey: cmd.groupKey } : {}),
    ...(cmd.occurredAtUtc !== undefined ? { occurredAtUtc: cmd.occurredAtUtc } : {}),
    sourceEventId: cmd.sourceEventId,
    sourceEventType: cmd.sourceEventType,
    correlationId: cmd.correlationId ?? null,
  });
  if (!creationResult.isSuccess) return Result.fail(creationResult.error);

  const notification = creationResult.value;
  const created = await deps.notifications.createIfMissing(notification);
  const unreadCount = await deps.notifications.countUnread(cmd.tenantId, cmd.userId);
  const asOfUtc = new Date().toISOString();

  if (!created) {
    return Result.ok({ created: false, notification: null, unreadCount, asOfUtc });
  }

  const dto: NotificationDto = toNotificationDto(notification.toSnapshot());
  return Result.ok({ created: true, notification: dto, unreadCount, asOfUtc });
}

/**
 * Input para {@link pushAndEmitNotification}: todo el contrato del dominio
 * (actor/resource/action/category/i18n) excepto los metadatos del envelope
 * (tenantId/sourceEventId/sourceEventType/correlationId), que se derivan del
 * `env` para evitar que cada consumer tenga que repetirlos. `userId` queda
 * explícito porque la mayoría de los consumers resuelve el destinatario
 * (owner/assignee/subscriber) aparte del CreatedBy del evento.
 */
export type BellNotificationInput = Omit<
  PushNotificationCommand,
  'tenantId' | 'sourceEventId' | 'sourceEventType' | 'correlationId'
>;

/**
 * Helper compartido para los 6 consumers legacy de Communication Node: persiste
 * la notificación por {@link pushNotification} y, si quedó creada (no duplicada),
 * emite al room del usuario `notification.received` + `notification.unread_count.changed`.
 *
 * Antes cada consumer duplicaba este patrón (ver `cloudstorage-consumers.ts`
 * `pushOwnerNotification` y gemelos en 5 archivos más); centralizarlo acá evita
 * que un cambio en el envelope del socket haya que replicarlo 6 veces.
 */
export async function pushAndEmitNotification(
  env: IncomingEnvelope,
  input: BellNotificationInput,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const result = await pushNotification(
    {
      ...input,
      tenantId: env.tenantId,
      sourceEventId: env.eventId,
      sourceEventType: env.eventType,
      correlationId: env.correlationId ?? null,
    },
    deps,
  );
  if (!result.isSuccess || !result.value.created || !result.value.notification) return;

  const asOfUtc = result.value.asOfUtc;
  const correlationId = env.correlationId ?? '';
  deps.emitter.emitToUser({
    tenantId: env.tenantId,
    userId: input.userId,
    event: NotificationSocketEvents.Received,
    envelope: {
      eventId: randomUUID(),
      correlationId,
      emittedAtUtc: asOfUtc,
      payload: result.value.notification,
    },
  });
  deps.emitter.emitToUser({
    tenantId: env.tenantId,
    userId: input.userId,
    event: NotificationSocketEvents.UnreadCountChanged,
    envelope: {
      eventId: randomUUID(),
      correlationId,
      emittedAtUtc: asOfUtc,
      payload: { count: result.value.unreadCount, asOfUtc },
    },
  });
}
