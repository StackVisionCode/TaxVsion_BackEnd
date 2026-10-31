import { randomUUID } from 'node:crypto';
import { logger } from '../../infrastructure/logger/logger.js';
import type { Conversation } from '../../domain/conversations/conversation.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { ChatSocketEvents, type ConversationUpdatedDto } from '../../contracts/socket/chat-socket-events.js';

export interface SetConversationArchivedDeps {
  readonly conversations: Pick<ConversationRepository, 'findById' | 'findByUniquenessKey' | 'save'>;
  readonly emitter?: RealtimeEmitter | undefined;
}

/**
 * F8a: el chat de un meeting que terminó (D-01) o de un ticket cerrado (D-13) queda en solo lectura; reabrir el
 * ticket lo reactiva. Avisa a quien lo tenga abierto (`chat.conversation.updated`) para que apague el composer.
 * Best-effort: un fallo aquí no tumba el cierre del meeting ni del ticket.
 */
export async function setConversationArchived(
  input: { tenantId: string; archived: boolean; conversationId?: string | null; uniquenessKey?: string | null; now?: Date },
  deps: SetConversationArchivedDeps,
): Promise<boolean> {
  try {
    const conversation = await findOwned(input, deps);
    if (!conversation) return false;
    const now = input.now ?? new Date();
    const changed = input.archived ? conversation.archive(now) : conversation.unarchive(now);
    if (!changed) return false;
    await deps.conversations.save(conversation);
    const dto: ConversationUpdatedDto = { conversationId: conversation.id, kind: conversation.kind, isArchived: conversation.isArchived };
    deps.emitter?.emitToConversation({
      tenantId: input.tenantId,
      conversationId: conversation.id,
      event: ChatSocketEvents.ConversationUpdated,
      envelope: { eventId: randomUUID(), correlationId: '', emittedAtUtc: now.toISOString(), payload: dto },
    });
    return true;
  } catch (err) {
    logger.warn({ err, tenantId: input.tenantId, archived: input.archived }, 'set conversation archived failed');
    return false;
  }
}

async function findOwned(
  input: { tenantId: string; conversationId?: string | null; uniquenessKey?: string | null },
  deps: SetConversationArchivedDeps,
): Promise<Conversation | null> {
  if (input.conversationId) {
    const byId = await deps.conversations.findById(input.tenantId, input.conversationId, 0);
    if (byId) return byId;
  }
  return input.uniquenessKey ? deps.conversations.findByUniquenessKey(input.tenantId, input.uniquenessKey) : null;
}
