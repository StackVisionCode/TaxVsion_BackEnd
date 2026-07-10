import type { Notification, NotificationSnapshot } from '../../domain/notifications/notification.js';

export interface NotificationRepository {
  /**
   * Insert-or-ignore por (TenantId, SourceEventId, UserId) — idempotencia
   * de consumers (cierra CRIT-10 legacy: mismo evento entregado dos veces = una
   * sola notificacion).
   * @returns `true` si se inserto; `false` si ya existia.
   */
  createIfMissing(notification: Notification): Promise<boolean>;

  findById(tenantId: string, id: string, userId: string): Promise<Notification | null>;

  update(tenantId: string, notification: Notification): Promise<void>;

  listForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
    unreadOnly?: boolean;
  }): Promise<NotificationSnapshot[]>;

  countUnread(tenantId: string, userId: string): Promise<number>;
}
