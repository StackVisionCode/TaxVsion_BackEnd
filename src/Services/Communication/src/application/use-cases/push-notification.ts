import { Result } from '../../domain/shared/result.js';
import { Notification, type NotificationPriority } from '../../domain/notifications/notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { NotificationDto } from '../../contracts/socket/notification-socket-events.js';

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
  readonly sourceEventId: string;
  readonly sourceEventType: string;
  readonly correlationId?: string | null;
}

export interface PushNotificationResult {
  readonly created: boolean;
  readonly notification: NotificationDto | null;
  readonly unreadCount: number;
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
    sourceEventId: cmd.sourceEventId,
    sourceEventType: cmd.sourceEventType,
    correlationId: cmd.correlationId ?? null,
  });
  if (!creationResult.isSuccess) return Result.fail(creationResult.error);

  const notification = creationResult.value;
  const created = await deps.notifications.createIfMissing(notification);
  const unreadCount = await deps.notifications.countUnread(cmd.tenantId, cmd.userId);

  if (!created) {
    return Result.ok({ created: false, notification: null, unreadCount });
  }

  const snap = notification.toSnapshot();
  const dto: NotificationDto = {
    id: snap.id,
    kind: snap.kind,
    priority: snap.priority,
    title: snap.title,
    body: snap.body,
    metadata: snap.metadata,
    createdAtUtc: snap.createdAtUtc.toISOString(),
    readAtUtc: null,
  };
  return Result.ok({ created: true, notification: dto, unreadCount });
}
