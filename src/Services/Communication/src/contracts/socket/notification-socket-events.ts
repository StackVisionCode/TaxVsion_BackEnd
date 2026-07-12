import { z } from 'zod';

/**
 * Notifications realtime. SEPARADO explicitamente de session events (session.revoked
 * y force.logout) para no repetir el bug del legacy que mezclaba ambos en el mismo
 * canal WS.
 */

// ---------- Client -> Server ----------

export const MarkNotificationReadPayloadSchema = z.object({
  notificationId: z.string().uuid(),
});
export type MarkNotificationReadPayload = z.infer<typeof MarkNotificationReadPayloadSchema>;

export const DismissNotificationPayloadSchema = z.object({
  notificationId: z.string().uuid(),
});
export type DismissNotificationPayload = z.infer<typeof DismissNotificationPayloadSchema>;

// ---------- Server -> Client ----------

export interface NotificationDto {
  id: string;
  kind: string;
  priority: 'Low' | 'Normal' | 'High' | 'Urgent';
  title: string;
  body: string;
  metadata: Readonly<Record<string, unknown>>;
  createdAtUtc: string;
  readAtUtc: string | null;
}

export interface NotificationUnreadCountDto {
  count: number;
}

export interface SessionRevokedDto {
  sessionId: string | null;
  jti: string | null;
  reason: string;
  revokedAtUtc: string;
}

export const NotificationSocketEvents = {
  // c -> s
  MarkRead: 'notification.mark_read',
  Dismiss: 'notification.dismiss',
  // s -> c (business)
  Received: 'notification.received',
  UnreadCountChanged: 'notification.unread_count.changed',
  ReadConfirmed: 'notification.read.confirmed',
  // s -> c (SESSION — canal propio, jamas mezclado con notifications de negocio)
  SessionRevoked: 'session.revoked',
} as const;
