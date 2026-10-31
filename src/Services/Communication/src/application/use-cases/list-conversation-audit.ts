import { Result, makeError } from '../../domain/shared/result.js';
import { isGroupAdminRole } from '../../domain/conversations/conversation-participant.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { ConversationAuditRepository } from '../ports/conversation-audit-repository.js';

export interface ConversationAuditItemDto {
  readonly id: string;
  readonly action: string;
  readonly actorUserId: string | null;
  readonly targetUserId: string | null;
  /** Nombre del actor y del afectado; también de quien ya salió del grupo (su fila se conserva). */
  readonly actorDisplayName: string | null;
  readonly targetDisplayName: string | null;
  readonly metadata: Readonly<Record<string, unknown>> | null;
  readonly occurredAtUtc: string;
}

/**
 * F8b (T-22): bitácora de un grupo. La leen quienes lo administran: su Owner/Admin o quien tenga el
 * superpoder de la oficina (Tenant Admin / `group.manage_members`), aunque no sea miembro.
 */
export async function listConversationAudit(
  query: { tenantId: string; conversationId: string; requesterUserId: string; canAdministerAnyGroup: boolean; take: number; beforeUtc?: string },
  deps: { conversations: ConversationRepository; conversationAudit: ConversationAuditRepository },
): Promise<Result<{ items: ConversationAuditItemDto[]; nextBeforeUtc: string | null }>> {
  const conversation = await deps.conversations.findById(query.tenantId, query.conversationId, 0);
  if (!conversation || conversation.kind !== 'Group') {
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }
  if (!query.canAdministerAnyGroup && !isGroupAdminRole(conversation.roleOf(query.requesterUserId))) {
    return Result.fail(makeError('Chat.Group.AdminOnly', 'Only group admins can see the group activity.'));
  }
  const take = Math.min(Math.max(query.take, 1), 100);
  const rows = await deps.conversationAudit.list({
    tenantId: query.tenantId,
    conversationId: query.conversationId,
    take,
    ...(query.beforeUtc ? { beforeUtc: new Date(query.beforeUtc) } : {}),
  });
  const names = new Map(conversation.getParticipantSnapshots().map((p) => [p.userId, p.displayName]));
  const nameOf = (userId: string | null) => (userId ? (names.get(userId) ?? null) : null);
  const items = rows.map((r) => ({
    id: r.id,
    action: r.action,
    actorUserId: r.actorUserId,
    targetUserId: r.targetUserId,
    actorDisplayName: nameOf(r.actorUserId),
    targetDisplayName: nameOf(r.targetUserId),
    metadata: r.metadata ?? null,
    occurredAtUtc: r.occurredAtUtc.toISOString(),
  }));
  return Result.ok({ items, nextBeforeUtc: rows.length === take ? (items.at(-1)?.occurredAtUtc ?? null) : null });
}
