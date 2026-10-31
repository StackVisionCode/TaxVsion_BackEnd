import { Result, makeError } from '../../domain/shared/result.js';
import { GALLERY_TABS, type GalleryTab } from '../../domain/attachments/attachment-category.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { ConversationContentRepository } from '../ports/conversation-content-repository.js';

export const CONTENT_PAGE_DEFAULT = 30;
export const CONTENT_PAGE_MAX = 50;

export type ConversationContentItem =
  | {
      readonly kind: 'Attachment';
      readonly fileId: string;
      readonly messageId: string;
      readonly senderId: string;
      readonly category: string;
      readonly contentType: string | null;
      readonly fileName: string | null;
      readonly sizeBytes: number | null;
      readonly status: string;
      readonly createdAtUtc: string;
    }
  | {
      readonly kind: 'Link';
      readonly id: string;
      readonly messageId: string;
      readonly senderId: string;
      readonly url: string;
      readonly domain: string;
      readonly createdAtUtc: string;
    };

/**
 * F8d (ADD-01 §13): una pestaña de Info (Media, Documents, Audio o Links) de una conversación donde participo,
 * paginada hacia atrás por fecha. El cliente de un grupo ve desde su ingreso (D-21).
 */
export async function listConversationContent(
  query: { tenantId: string; conversationId: string; requesterUserId: string; tab: GalleryTab; before?: string | null; take?: number },
  deps: { conversations: ConversationRepository; conversationContent: ConversationContentRepository },
): Promise<Result<{ items: ConversationContentItem[]; nextBefore: string | null }>> {
  const conversation = await deps.conversations.findById(query.tenantId, query.conversationId, 0);
  if (!conversation) return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  if (!conversation.isParticipant(query.requesterUserId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }
  const take = Math.min(Math.max(query.take ?? CONTENT_PAGE_DEFAULT, 1), CONTENT_PAGE_MAX);
  const window = {
    tenantId: query.tenantId,
    conversationId: query.conversationId,
    take,
    before: parseContentCursor(query.before),
    notBeforeUtc: conversation.historyVisibleFrom(query.requesterUserId),
  };

  let items: ConversationContentItem[];
  if (query.tab === 'Links') {
    const links = await deps.conversationContent.listLinks(window);
    items = links.map((l) => ({ kind: 'Link', id: l.id, messageId: l.messageId, senderId: l.senderId, url: l.url, domain: l.domain, createdAtUtc: l.createdAtUtc.toISOString() }));
  } else {
    const attachments = await deps.conversationContent.listAttachments({ ...window, categories: GALLERY_TABS[query.tab] });
    items = attachments.map((a) => ({
      kind: 'Attachment',
      fileId: a.fileId,
      messageId: a.messageId,
      senderId: a.senderId,
      category: a.category,
      contentType: a.contentType,
      fileName: a.fileName,
      sizeBytes: a.sizeBytes,
      status: a.status,
      createdAtUtc: a.createdAtUtc.toISOString(),
    }));
  }
  const last = items.at(-1);
  const next = items.length === take && last ? `${last.createdAtUtc}|${last.kind === 'Link' ? last.id : last.fileId}` : null;
  return Result.ok({ items, nextBefore: next });
}

/** Cursor `fecha|id`; uno inválido se ignora (primera página), nunca rompe la consulta. */
function parseContentCursor(raw: string | null | undefined): { at: Date; id: string } | null {
  if (!raw) return null;
  const [iso, id] = raw.split('|');
  const at = iso ? new Date(iso) : null;
  return at && !Number.isNaN(at.getTime()) && id && /^[0-9a-f-]{36}$/iu.test(id) ? { at, id } : null;
}
