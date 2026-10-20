import { Result, makeError } from '../../domain/shared/result.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { NotificationDto } from '../../contracts/socket/notification-socket-events.js';
import { toNotificationDto } from './notification-dto.js';

export interface ListNotificationsQuery {
  readonly tenantId: string;
  readonly userId: string;
  readonly page: number;
  readonly size: number;
  readonly unreadOnly?: boolean;
}

export interface ListNotificationsSinceQuery {
  readonly tenantId: string;
  readonly userId: string;
  readonly sinceUtc: Date;
  readonly limit: number;
}

export interface ListNotificationsSinceResult {
  readonly items: readonly NotificationDto[];
  readonly unreadCount: number;
  readonly asOfUtc: string;
  /**
   * `CreatedAtUtc` del último item devuelto (ISO). Null si items está vacío.
   * El cliente lo guarda y lo manda como `sinceUtc` en la próxima llamada —
   * es el "cursor" implícito. Si items.length === limit, el cliente llama de
   * nuevo con este valor para traer la siguiente página.
   */
  readonly nextCursorUtc: string | null;
}

export async function listNotificationsSince(
  query: ListNotificationsSinceQuery,
  deps: { notifications: NotificationRepository },
): Promise<Result<ListNotificationsSinceResult>> {
  if (Number.isNaN(query.sinceUtc.getTime())) {
    return Result.fail(makeError('Notification.InvalidSince', 'The `sinceUtc` cursor is invalid.'));
  }
  const limit = Math.min(Math.max(query.limit, 1), 200);
  const [snapshots, unreadCount] = await Promise.all([
    deps.notifications.listSinceForUser({
      tenantId: query.tenantId,
      userId: query.userId,
      sinceUtc: query.sinceUtc,
      limit,
    }),
    deps.notifications.countUnread(query.tenantId, query.userId),
  ]);
  const last = snapshots.length > 0 ? snapshots[snapshots.length - 1] : null;
  return Result.ok({
    items: snapshots.map(toNotificationDto),
    unreadCount,
    asOfUtc: new Date().toISOString(),
    nextCursorUtc: last ? last.createdAtUtc.toISOString() : null,
  });
}

export interface ListNotificationsResult {
  readonly items: readonly NotificationDto[];
  readonly page: number;
  readonly size: number;
  /** Total de filas que cumplen el mismo filtro (`unreadOnly`): para "Page X of Y" sin cursor. */
  readonly total: number;
  readonly unreadCount: number;
  readonly asOfUtc: string;
}

export async function listNotifications(
  query: ListNotificationsQuery,
  deps: { notifications: NotificationRepository },
): Promise<Result<ListNotificationsResult>> {
  const size = Math.min(Math.max(query.size, 1), 100);
  const page = Math.max(query.page, 1);
  const [snapshots, total, unreadCount] = await Promise.all([
    deps.notifications.listForUser({
      tenantId: query.tenantId,
      userId: query.userId,
      take: size,
      skip: (page - 1) * size,
      ...(query.unreadOnly !== undefined ? { unreadOnly: query.unreadOnly } : {}),
    }),
    deps.notifications.countForUser({
      tenantId: query.tenantId,
      userId: query.userId,
      ...(query.unreadOnly !== undefined ? { unreadOnly: query.unreadOnly } : {}),
    }),
    deps.notifications.countUnread(query.tenantId, query.userId),
  ]);
  return Result.ok({
    items: snapshots.map(toNotificationDto),
    page,
    size,
    total,
    unreadCount,
    asOfUtc: new Date().toISOString(),
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
  readonly asOfUtc: string;
}

export async function markNotificationRead(
  cmd: MarkNotificationReadCommand,
  deps: { notifications: NotificationRepository },
): Promise<Result<MarkNotificationReadResult>> {
  const found = await deps.notifications.markRead(cmd.tenantId, cmd.userId, cmd.notificationId, new Date());
  if (!found) {
    return Result.fail(makeError('Notification.NotFound', 'Notification not found.'));
  }
  const unreadCount = await deps.notifications.countUnread(cmd.tenantId, cmd.userId);
  return Result.ok({ notificationId: cmd.notificationId, unreadCount, asOfUtc: new Date().toISOString() });
}

export interface DismissNotificationCommand {
  readonly tenantId: string;
  readonly userId: string;
  readonly notificationId: string;
}

export interface DismissNotificationResult {
  readonly notificationId: string;
  readonly unreadCount: number;
  readonly asOfUtc: string;
}

/**
 * Fase Backend 11 — el dominio (Notification.dismiss(), ya existente) y la
 * columna DismissedAtUtc no tenian ningun use case/socket que los expusiera;
 * `listForUser`/`countUnread` ya excluyen dismissed (ver PrismaNotificationRepository),
 * asi que dismiss oculta la notificacion de la lista sin borrarla.
 */
export async function dismissNotification(
  cmd: DismissNotificationCommand,
  deps: { notifications: NotificationRepository },
): Promise<Result<DismissNotificationResult>> {
  const found = await deps.notifications.dismiss(cmd.tenantId, cmd.userId, cmd.notificationId, new Date());
  if (!found) {
    return Result.fail(makeError('Notification.NotFound', 'Notification not found.'));
  }
  const unreadCount = await deps.notifications.countUnread(cmd.tenantId, cmd.userId);
  return Result.ok({ notificationId: cmd.notificationId, unreadCount, asOfUtc: new Date().toISOString() });
}

export interface MarkAllNotificationsReadCommand {
  readonly tenantId: string;
  readonly userId: string;
  readonly cutoffUtc?: Date;
}

export interface MarkAllNotificationsReadResult {
  readonly cutoffUtc: string;
  readonly markedCount: number;
  readonly unreadCount: number;
  readonly asOfUtc: string;
}

export async function markAllNotificationsRead(
  cmd: MarkAllNotificationsReadCommand,
  deps: { notifications: NotificationRepository },
): Promise<Result<MarkAllNotificationsReadResult>> {
  const readAtUtc = new Date();
  const cutoffUtc = cmd.cutoffUtc ?? readAtUtc;
  if (Number.isNaN(cutoffUtc.getTime())) {
    return Result.fail(makeError('Notification.InvalidCutoff', 'The notification cutoff is invalid.'));
  }
  const result = await deps.notifications.markAllRead({
    tenantId: cmd.tenantId,
    userId: cmd.userId,
    cutoffUtc,
    readAtUtc,
  });
  return Result.ok({
    cutoffUtc: cutoffUtc.toISOString(),
    markedCount: result.markedCount,
    unreadCount: result.unreadCount,
    asOfUtc: new Date().toISOString(),
  });
}

export interface BulkNotificationCommand {
  readonly tenantId: string;
  readonly userId: string;
  readonly ids: readonly string[];
}

export interface BulkNotificationResult {
  readonly affectedCount: number;
  readonly unreadCount: number;
  readonly asOfUtc: string;
}

const MAX_BULK = 200;

function validateBulkIds(ids: readonly string[]): Result<readonly string[]> {
  if (ids.length === 0) {
    return Result.fail(makeError('Notification.BulkEmpty', 'No notification ids were provided.'));
  }
  if (ids.length > MAX_BULK) {
    return Result.fail(
      makeError('Notification.BulkTooLarge', `A single bulk operation can include at most ${MAX_BULK} ids.`),
    );
  }
  // Dedupe silencioso: evita contar dos veces el mismo id en el affectedCount.
  const deduped = Array.from(new Set(ids));
  return Result.ok(deduped);
}

/** Marca varias notificaciones como leídas (ignora las ya leídas o dismissed). */
export async function markNotificationsRead(
  cmd: BulkNotificationCommand,
  deps: { notifications: NotificationRepository },
): Promise<Result<BulkNotificationResult>> {
  const idsResult = validateBulkIds(cmd.ids);
  if (!idsResult.isSuccess) return idsResult;
  const result = await deps.notifications.markManyRead({
    tenantId: cmd.tenantId,
    userId: cmd.userId,
    ids: idsResult.value,
    readAtUtc: new Date(),
  });
  return Result.ok({
    affectedCount: result.markedCount,
    unreadCount: result.unreadCount,
    asOfUtc: new Date().toISOString(),
  });
}

/** Devuelve varias filas a no-leídas (ignora las ya no-leídas o dismissed). */
export async function markNotificationsUnread(
  cmd: BulkNotificationCommand,
  deps: { notifications: NotificationRepository },
): Promise<Result<BulkNotificationResult>> {
  const idsResult = validateBulkIds(cmd.ids);
  if (!idsResult.isSuccess) return idsResult;
  const result = await deps.notifications.markManyUnread({
    tenantId: cmd.tenantId,
    userId: cmd.userId,
    ids: idsResult.value,
  });
  return Result.ok({
    affectedCount: result.unmarkedCount,
    unreadCount: result.unreadCount,
    asOfUtc: new Date().toISOString(),
  });
}

/**
 * "Delete" desde la UI = soft-dismiss (listForUser excluye dismissed). No hay hard delete
 * para preservar idempotencia de consumers y audit trail.
 */
export async function deleteNotifications(
  cmd: BulkNotificationCommand,
  deps: { notifications: NotificationRepository },
): Promise<Result<BulkNotificationResult>> {
  const idsResult = validateBulkIds(cmd.ids);
  if (!idsResult.isSuccess) return idsResult;
  const result = await deps.notifications.dismissMany({
    tenantId: cmd.tenantId,
    userId: cmd.userId,
    ids: idsResult.value,
    dismissedAtUtc: new Date(),
  });
  return Result.ok({
    affectedCount: result.dismissedCount,
    unreadCount: result.unreadCount,
    asOfUtc: new Date().toISOString(),
  });
}
