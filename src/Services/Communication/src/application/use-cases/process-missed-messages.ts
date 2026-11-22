import { randomUUID } from 'node:crypto';
import type { MissedMessagesCandidate, MissedMessagesStore } from '../ports/missed-messages-store.js';
import { ChatEventTypes, type ChatMessagesMissedEvent } from '../../contracts/events/chat-events.js';

/** Solo conversaciones con mensajes en la última hora: el job no recorre todos los participantes. */
const ACTIVE_WINDOW_MINUTES = 60;
const BATCH_SIZE = 200;

/**
 * Racha nueva: nunca se avisó, o el usuario leyó después del último aviso y lo no leído es posterior.
 * Mientras la racha avisada siga sin leer no se vuelve a avisar.
 */
export function isNewMissedStreak(candidate: MissedMessagesCandidate): boolean {
  const notice = candidate.missedNoticeAtUtc;
  if (notice === null) return true;
  return (
    candidate.oldestUnreadAtUtc > notice && candidate.lastReadAtUtc !== null && candidate.lastReadAtUtc >= notice
  );
}

/** D-N4: avisa una vez por racha de mensajes que siguen sin leer pasados `afterMinutes` (0 = apagado). */
export async function processMissedMessages(
  input: { readonly now?: Date; readonly afterMinutes: number },
  deps: { readonly store: MissedMessagesStore },
): Promise<number> {
  if (input.afterMinutes <= 0) return 0;
  const now = input.now ?? new Date();
  const unreadBeforeUtc = new Date(now.getTime() - input.afterMinutes * 60_000);
  const lastMessageSinceUtc = new Date(unreadBeforeUtc.getTime() - ACTIVE_WINDOW_MINUTES * 60_000);
  const candidates = await deps.store.findCandidates({ lastMessageSinceUtc, unreadBeforeUtc, limit: BATCH_SIZE });

  let published = 0;
  for (const candidate of candidates.filter(isNewMissedStreak)) {
    const event: ChatMessagesMissedEvent = {
      eventId: randomUUID(),
      eventType: ChatEventTypes.MessagesMissed,
      tenantId: candidate.tenantId,
      // Sin envelope entrante: la conversación enlaza los logs con el consumer.
      correlationId: candidate.conversationId,
      occurredOnUtc: now.toISOString(),
      conversationId: candidate.conversationId,
      conversationKind: candidate.conversationKind,
      recipientUserId: candidate.userId,
      unreadCount: candidate.unreadCount,
      oldestUnreadAtUtc: candidate.oldestUnreadAtUtc.toISOString(),
      latestMessageId: candidate.latestMessageId,
      latestSenderId: candidate.latestSenderId,
    };
    if (await deps.store.recordNotice({ candidate, noticedAtUtc: now, event })) published += 1;
  }
  return published;
}
