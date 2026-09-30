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

/**
 * El acceso del cliente cambio: sus permisos, o los modulos que el plan de la oficina habilita.
 * **No lleva datos** a proposito — solo dice "volve a pedir el bootstrap" (`GET /auth/me/access`).
 * Meter los permisos en el payload obligaria a mantener dos caminos de verdad y a decidir que hacer
 * si llegan desordenados.
 */
export interface AccessChangedDto {
  /** `user` = cambiaron los permisos de este usuario. `tenant` = cambiaron los modulos de la oficina. */
  scope: 'user' | 'tenant';
  /** `perm_v` nuevo, cuando el cambio es del usuario. Permite ignorar un evento viejo sin refetch. */
  permissionsVersion: number | null;
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
  // s -> c (ACCESO — mismo criterio: canal propio, sin datos de negocio en el payload)
  AccessChanged: 'access.changed',
} as const;
