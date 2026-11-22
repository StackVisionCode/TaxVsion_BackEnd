import type { Notification, NotificationSnapshot } from '../../domain/notifications/notification.js';
import type { NotificationListFilter } from '../../domain/notifications/notification-category.js';
import type { NotificationBulkAction } from '../../contracts/socket/notification-socket-events.js';

export interface NotificationRepository {
  /**
   * Insert-or-ignore por (TenantId, SourceEventId, UserId) — idempotencia
   * de consumers (cierra CRIT-10 legacy: mismo evento entregado dos veces = una
   * sola notificacion).
   * @returns `true` si se inserto; `false` si ya existia.
   */
  createIfMissing(notification: Notification): Promise<boolean>;

  findById(tenantId: string, id: string, userId: string): Promise<Notification | null>;

  /** Por la clave única (oficina, evento de origen, usuario). */
  findBySourceEvent?(tenantId: string, sourceEventId: string, userId: string): Promise<Notification | null>;

  markRead(tenantId: string, userId: string, id: string, readAtUtc: Date): Promise<boolean>;

  /** Vuelve a dejar la fila como no leída (ReadAtUtc = null) si pertenece al usuario. */
  markUnread(tenantId: string, userId: string, id: string): Promise<boolean>;

  dismiss(tenantId: string, userId: string, id: string, dismissedAtUtc: Date): Promise<boolean>;

  markAllRead(input: {
    tenantId: string;
    userId: string;
    cutoffUtc: Date;
    readAtUtc: Date;
  }): Promise<{ markedCount: number; unreadCount: number }>;

  /**
   * Marca varias filas como leídas en una sola transacción. Devuelve además el
   * conteo final de no-leídas para que el handler emita el socket sin un GET extra.
   */
  markManyRead(input: {
    tenantId: string;
    userId: string;
    ids: readonly string[];
    readAtUtc: Date;
  }): Promise<{ markedCount: number; unreadCount: number }>;

  /** Filas sin leer del usuario con ese `GroupKey` (p. ej. "mensajes perdidos" de un chat). */
  findUnreadIdsByGroupKey?(input: {
    tenantId: string;
    userId: string;
    groupKey: string;
  }): Promise<readonly string[]>;

  /** Simétrico de markManyRead: vuelve varias filas a no-leídas. */
  markManyUnread(input: {
    tenantId: string;
    userId: string;
    ids: readonly string[];
  }): Promise<{ unmarkedCount: number; unreadCount: number }>;

  /**
   * "Delete" desde la UI = soft-dismiss (listForUser ya excluye dismissed).
   * Preservamos la fila para auditoría/idempotencia y no agregamos un hard delete.
   */
  dismissMany(input: {
    tenantId: string;
    userId: string;
    ids: readonly string[];
    dismissedAtUtc: Date;
  }): Promise<{ dismissedCount: number; unreadCount: number }>;

  listForUser(
    input: {
      tenantId: string;
      userId: string;
      take: number;
      skip: number;
    } & NotificationListFilter,
  ): Promise<NotificationSnapshot[]>;

  /** Conteo total para paginación: respeta los mismos filtros que `listForUser`. */
  countForUser(input: { tenantId: string; userId: string } & NotificationListFilter): Promise<number>;

  /**
   * Aplica la acción a las filas del usuario que cumplen el filtro, no descartadas y con
   * `CreatedAtUtc <= cutoffUtc`, en lotes. Devuelve las afectadas y el conteo final de no leídas.
   */
  bulkByFilter?(input: {
    tenantId: string;
    userId: string;
    action: NotificationBulkAction;
    filter: NotificationListFilter;
    cutoffUtc: Date;
    atUtc: Date;
  }): Promise<{ affectedCount: number; unreadCount: number }>;

  countUnread(tenantId: string, userId: string): Promise<number>;

  /**
   * Devuelve notificaciones con `CreatedAtUtc > sinceUtc`, orden ascendente
   * por `CreatedAtUtc` + `Id` (tiebreaker para evitar duplicados cuando dos
   * filas comparten timestamp al milisegundo). Usado por el endpoint `/since`
   * para resync tras una reconexión de socket: el cliente guarda el último
   * `CreatedAtUtc` que vio y pide "lo que llegó después". Siempre excluye
   * `DismissedAtUtc`. Devuelve a lo sumo `limit` filas; si vinieron `limit`
   * exactas, el cliente puede pedir otra página con el `CreatedAtUtc` de la
   * última.
   */
  listSinceForUser(input: {
    tenantId: string;
    userId: string;
    sinceUtc: Date;
    /** Desempate opcional: con él, el cursor es `(sinceUtc, sinceId)` y no salta filas del mismo milisegundo. */
    sinceId?: string;
    limit: number;
  }): Promise<NotificationSnapshot[]>;
}
