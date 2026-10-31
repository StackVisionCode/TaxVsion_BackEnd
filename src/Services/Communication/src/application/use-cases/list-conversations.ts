import { Result } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { ConversationKind } from '../../domain/conversations/conversation-kind.js';
import type { ConversationSnapshot } from '../../domain/conversations/conversation.js';
import type { GroupRole } from '../../domain/conversations/conversation-participant.js';
import { resolveAvatars, type AvatarResolver } from './resolve-avatars.js';

export interface ListConversationsQuery {
  readonly tenantId: string;
  readonly userId: string;
  readonly page: number;
  readonly size: number;
  readonly includeArchived?: boolean;
  /** F8a: solo estos tipos (Chat = Direct,Group; Support aparte). */
  readonly kinds?: readonly ConversationKind[];
}

export interface ConversationParticipantListItem {
  readonly userId: string;
  readonly displayName: string;
  readonly isPrimaryPreparer: boolean;
  /** F5: foto resuelta al leer (staff → Auth, portal → ficha). Ausente si no se pudo resolver; null = sin foto. */
  readonly avatarFileId?: string | null;
  /** F8b (D-22): rol en el grupo y tipo de actor (los clientes nunca administran). */
  readonly role: GroupRole;
  readonly actorType: string;
}

export interface ConversationListItem {
  readonly id: string;
  readonly kind: 'Direct' | 'Group' | 'Support' | 'Meeting';
  readonly title: string | null;
  readonly lastMessageAtUtc: string | null;
  readonly updatedAtUtc: string;
  readonly participants: readonly ConversationParticipantListItem[];
  readonly unreadCount: number;
  /** F8b: datos del grupo (T-21), modo anuncio (D-23), archivado y estado propio. */
  readonly description: string | null;
  readonly avatarFileId: string | null;
  readonly adminsOnlyWrite: boolean;
  readonly isArchived: boolean;
  readonly isMuted: boolean;
  readonly myRole: GroupRole | null;
}

/** Una fila del listado; la usan el listado y el ack de `start_group` (T-12). */
export function toConversationListItem(
  snapshot: ConversationSnapshot,
  viewerUserId: string,
  unreadCount: number,
  avatarOf?: (userId: string) => string | null,
): ConversationListItem {
  const active = snapshot.participants.filter((p) => !p.isRemoved);
  const me = active.find((p) => p.userId === viewerUserId);
  return {
    id: snapshot.id,
    kind: snapshot.kind,
    title: snapshot.title,
    lastMessageAtUtc: snapshot.lastMessageAtUtc ? snapshot.lastMessageAtUtc.toISOString() : null,
    updatedAtUtc: snapshot.updatedAtUtc.toISOString(),
    participants: active.map((p) => ({
      userId: p.userId,
      displayName: p.displayName,
      isPrimaryPreparer: p.isPrimaryPreparer,
      role: p.role,
      actorType: p.actorType,
      ...(avatarOf ? { avatarFileId: avatarOf(p.userId) } : {}),
    })),
    unreadCount,
    description: snapshot.description ?? null,
    avatarFileId: snapshot.avatarFileId ?? null,
    adminsOnlyWrite: snapshot.adminsOnlyWrite ?? false,
    isArchived: snapshot.isArchived,
    isMuted: me?.isMuted ?? false,
    myRole: me?.role ?? null,
  };
}

export interface ListConversationsResult {
  readonly items: readonly ConversationListItem[];
  readonly page: number;
  readonly size: number;
  readonly totalCount: number;
  /** F8a: no leídos por tipo en todas las conversaciones activas (no solo la página). */
  readonly unreadByKind?: Readonly<Record<ConversationKind, number>>;
}

export async function listConversations(
  query: ListConversationsQuery,
  deps: { conversations: ConversationRepository; avatars?: AvatarResolver },
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
      ...(query.kinds ? { kinds: query.kinds } : {}),
    }),
    deps.conversations.countForUser(query.tenantId, query.userId, query.includeArchived ?? false, query.kinds),
  ]);
  const unreadByKind = await deps.conversations.countUnreadByKind(query.tenantId, query.userId);

  // Una sola consulta de fotos para todos los participantes de la pagina.
  const avatars = await resolveAvatars(
    deps.avatars,
    snapshots.flatMap((snapshot) => snapshot.participants.filter((p) => !p.isRemoved).map((p) => p.userId)),
  );

  const items = await Promise.all(
    snapshots.map(async (snapshot): Promise<ConversationListItem> => {
      const unread = await deps.conversations.countUnreadForUser({
        tenantId: query.tenantId,
        conversationId: snapshot.id,
        userId: query.userId,
      });
      return toConversationListItem(snapshot, query.userId, unread, avatars ? (id) => avatars.of(id) : undefined);
    }),
  );

  return Result.ok({ items, page, size, totalCount, unreadByKind });
}
