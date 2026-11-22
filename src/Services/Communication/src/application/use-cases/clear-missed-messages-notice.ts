import type { NotificationRepository } from '../ports/notification-repository.js';

/** Misma clave que escribe Notification: `chat:{conversationId:N}` (sin guiones, minúsculas). */
export function missedMessagesGroupKey(conversationId: string): string {
  return `chat:${conversationId.replace(/-/g, '').toLowerCase()}`;
}

/** Al leer el chat, la fila de "mensajes perdidos" de esa conversación queda leída. Devuelve sus ids. */
export async function clearMissedMessagesNotice(
  input: { readonly tenantId: string; readonly userId: string; readonly conversationId: string },
  deps: { readonly notifications: Pick<NotificationRepository, 'findUnreadIdsByGroupKey' | 'markManyRead'> },
): Promise<readonly string[]> {
  const ids =
    (await deps.notifications.findUnreadIdsByGroupKey?.({
      tenantId: input.tenantId,
      userId: input.userId,
      groupKey: missedMessagesGroupKey(input.conversationId),
    })) ?? [];
  if (ids.length === 0) return [];
  await deps.notifications.markManyRead({ tenantId: input.tenantId, userId: input.userId, ids, readAtUtc: new Date() });
  return ids;
}
