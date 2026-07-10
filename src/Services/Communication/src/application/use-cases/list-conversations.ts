import { Result } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';

export interface ListConversationsQuery {
  readonly tenantId: string;
  readonly userId: string;
  readonly page: number;
  readonly size: number;
  readonly includeArchived?: boolean;
}

export interface ConversationListItem {
  readonly id: string;
  readonly kind: 'Direct' | 'Group' | 'Support';
  readonly title: string | null;
  readonly lastMessageAtUtc: string | null;
  readonly updatedAtUtc: string;
  readonly participants: readonly { userId: string; displayName: string; isPrimaryPreparer: boolean }[];
  readonly unreadCount: number;
}

export interface ListConversationsResult {
  readonly items: readonly ConversationListItem[];
  readonly page: number;
  readonly size: number;
  readonly totalCount: number;
}

export async function listConversations(
  query: ListConversationsQuery,
  deps: { conversations: ConversationRepository },
): Promise<Result<ListConversationsResult>> {
  const size = Math.min(Math.max(query.size, 1), 100);
  const page = Math.max(query.page, 1);

  const [snapshots, totalCount] = await Promise.all([
    deps.conversations.listForUser({
      tenantId: query.tenantId,
      userId: query.userId,
      take: size,
      skip: (page - 1) * size,
      includeArchived: query.includeArchived ?? false,
    }),
    deps.conversations.countForUser(query.tenantId, query.userId, query.includeArchived ?? false),
  ]);

  const items = await Promise.all(
    snapshots.map(async (snapshot): Promise<ConversationListItem> => {
      const unread = await deps.conversations.countUnreadForUser({
        tenantId: query.tenantId,
        conversationId: snapshot.id,
        userId: query.userId,
      });
      return {
        id: snapshot.id,
        kind: snapshot.kind,
        title: snapshot.title,
        lastMessageAtUtc: snapshot.lastMessageAtUtc ? snapshot.lastMessageAtUtc.toISOString() : null,
        updatedAtUtc: snapshot.updatedAtUtc.toISOString(),
        participants: snapshot.participants
          .filter((p) => !p.isRemoved)
          .map((p) => ({
            userId: p.userId,
            displayName: p.displayName,
            isPrimaryPreparer: p.isPrimaryPreparer,
          })),
        unreadCount: unread,
      };
    }),
  );

  return Result.ok({ items, page, size, totalCount });
}
