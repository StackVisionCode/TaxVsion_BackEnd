import { randomUUID } from 'node:crypto';
import { extractMessageLinks } from '../../domain/conversations/message-links.js';
import type { MessageSnapshot } from '../../domain/conversations/message.js';

/** F8d (T-16): filas de `MessageLink` de un mensaje (solo texto vivo); la fecha es la del mensaje. */
export function messageLinkRows(m: Pick<MessageSnapshot, 'id' | 'tenantId' | 'conversationId' | 'kind' | 'body' | 'isDeleted' | 'createdAtUtc'>) {
  if (m.kind !== 'Text' || m.isDeleted) return [];
  return extractMessageLinks(m.body).map((link) => ({
    Id: randomUUID(),
    TenantId: m.tenantId,
    ConversationId: m.conversationId,
    MessageId: m.id,
    Url: link.url,
    Domain: link.domain,
    CreatedAtUtc: m.createdAtUtc,
  }));
}
