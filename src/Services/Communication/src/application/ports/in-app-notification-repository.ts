import type { Notification, NotificationSnapshot } from '../../domain/notifications/notification.js';

export type InAppNotificationPersistenceStatus = 'created' | 'updated' | 'duplicate';

export interface InAppNotificationPersistenceResult {
  readonly status: InAppNotificationPersistenceStatus;
  readonly notification: NotificationSnapshot | null;
}

export interface InAppNotificationRepository {
  persist(notification: Notification): Promise<InAppNotificationPersistenceResult>;
  countUnread(tenantId: string, userId: string): Promise<number>;
}
