import { z } from 'zod';
import type { NotificationListFilter } from '../../domain/notifications/notification-category.js';

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
  category: string | null;
  actorUserId: string | null;
  actorDisplayName: string | null;
  /**
   * "System" | "TenantEmployee" | "TenantAdmin" | "CustomerPortal" | "External".
   * Permite distinguir "lo hizo el sistema" vs "lo hizo un usuario" sin resolver
   * actorUserId contra directory. Null para notificaciones legacy pre-contrato.
   */
  actorType: string | null;
  resourceType: string | null;
  resourceId: string | null;
  resourceContext: Readonly<Record<string, string>>;
  titleKey: string | null;
  bodyKey: string | null;
  params: Readonly<Record<string, string>>;
  action: string | null;
  groupKey: string | null;
  occurredAtUtc: string | null;
  createdAtUtc: string;
  readAtUtc: string | null;
}

export interface NotificationUnreadCountDto {
  count: number;
  asOfUtc: string;
}

export type NotificationBulkAction = 'read' | 'unread' | 'dismissed';

/** Qué filas cambió una acción masiva o un descarte; va a todas las sesiones del mismo usuario. */
export interface NotificationBulkAppliedDto {
  action: NotificationBulkAction;
  /** Ids pedidos, ya sin repetidos (1..200). Vacío con `scope: 'filter'`. */
  notificationIds: readonly string[];
  asOfUtc: string;
  /** Ausente = 'ids'. Con 'filter' el cliente vuelve a pedir su página. */
  scope?: 'ids' | 'filter';
  filter?: NotificationListFilter;
  cutoffUtc?: string;
  affectedCount?: number;
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
  Updated: 'notification.updated',
  UnreadCountChanged: 'notification.unread_count.changed',
  BulkApplied: 'notification.bulk.applied',
  ReadConfirmed: 'notification.read.confirmed',
  ReadAllConfirmed: 'notification.read_all.confirmed',
  // s -> c (SESSION — canal propio, jamas mezclado con notifications de negocio)
  SessionRevoked: 'session.revoked',
  // s -> c (ACCESO — mismo criterio: canal propio, sin datos de negocio en el payload)
  AccessChanged: 'access.changed',
  // s -> c (OFICINA — P27: la zona cambió; payload { timeZoneId } y cada sesión recarga su perfil)
  OfficeTimeZoneChanged: 'office.time_zone.changed',
} as const;
