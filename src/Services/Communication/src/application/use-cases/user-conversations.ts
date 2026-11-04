import type { ConversationSnapshot } from '../../domain/conversations/conversation.js';
import type { ConversationKind } from '../../domain/conversations/conversation-kind.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';

/** Tamaño de página al recorrer las conversaciones de un usuario en procesos por sistema. */
const USER_CONVERSATIONS_PAGE = 200;

/**
 * Todas las conversaciones (de esos tipos) donde el usuario sigue dentro, página a página. Se juntan antes de
 * mutar para que la paginación no salte filas al ir sacándolo (retiro, pre-flight).
 */
export async function listUserConversations(
  conversations: Pick<ConversationRepository, 'listForUser'>,
  input: { tenantId: string; userId: string; kinds: readonly ConversationKind[]; includeArchived: boolean },
): Promise<ConversationSnapshot[]> {
  const byId = new Map<string, ConversationSnapshot>();
  for (let skip = 0; ; skip += USER_CONVERSATIONS_PAGE) {
    const page = await conversations.listForUser({
      tenantId: input.tenantId,
      userId: input.userId,
      take: USER_CONVERSATIONS_PAGE,
      skip,
      includeArchived: input.includeArchived,
      kinds: input.kinds,
    });
    for (const snapshot of page) if (!byId.has(snapshot.id)) byId.set(snapshot.id, snapshot);
    if (page.length < USER_CONVERSATIONS_PAGE) return [...byId.values()];
  }
}
