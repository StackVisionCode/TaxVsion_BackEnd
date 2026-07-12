import { Result, makeError } from '../../domain/shared/result.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { NotificationDto } from '../../contracts/socket/notification-socket-events.js';

export interface ListNotificationsQuery {
  readonly tenantId: string;
  readonly userId: string;
  readonly page: number;
  readonly size: number;
  readonly unreadOnly?: boolean;
}

export interface ListNotificationsResult {
  readonly items: readonly NotificationDto[];
  readonly page: number;
  readonly size: number;
  readonly unreadCount: number;
}

export async function listNotifications(
  query: ListNotificationsQuery,
  deps: { notifications: NotificationRepository },
): Promise<Result<ListNotificationsResult>> {
  const size = Math.min(Math.max(query.size, 1), 100);
  const page = Math.max(query.page, 1);
  const [snapshots, unreadCount] = await Promise.all([
    deps.notifications.listForUser({
      tenantId: query.tenantId,
      userId: query.userId,
      take: size,
      skip: (page - 1) * size,
      ...(query.unreadOnly !== undefined ? { unreadOnly: query.unreadOnly } : {}),
    }),
    deps.notifications.countUnread(query.tenantId, query.userId),
  ]);
  return Result.ok({
    items: snapshots.map((s) => ({
      id: s.id,
      kind: s.kind,
      priority: s.priority,
      title: s.title,
      body: s.body,
      metadata: s.metadata,
      createdAtUtc: s.createdAtUtc.toISOString(),
      readAtUtc: s.readAtUtc ? s.readAtUtc.toISOString() : null,
    })),
    page,
    size,
    unreadCount,
  });
}

export interface MarkNotificationReadCommand {
  readonly tenantId: string;
  readonly userId: string;
  readonly notificationId: string;
}

export interface MarkNotificationReadResult {
  readonly notificationId: string;
  readonly unreadCount: number;
}

export async function markNotificationRead(
  cmd: MarkNotificationReadCommand,
  deps: { notifications: NotificationRepository },
): Promise<Result<MarkNotificationReadResult>> {
  const notification = await deps.notifications.findById(cmd.tenantId, cmd.notificationId, cmd.userId);
  if (!notification) {
    return Result.fail(makeError('Notification.NotFound', 'Notification not found.'));
  }
  notification.markRead();
  await deps.notifications.update(cmd.tenantId, notification);
  const unreadCount = await deps.notifications.countUnread(cmd.tenantId, cmd.userId);
  return Result.ok({ notificationId: cmd.notificationId, unreadCount });
}
