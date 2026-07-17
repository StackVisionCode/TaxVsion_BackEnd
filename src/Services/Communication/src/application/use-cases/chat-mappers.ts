import type { MessageSnapshot } from '../../domain/conversations/message.js';
import type { MessageDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Snapshot → wire DTO. Se centraliza aca para no duplicar el shape en cada
 * use case que emite mensajes al socket (send, forward, get-messages, search).
 * Los soft-deleted no pierden metadata aca — cada caller decide si redactar
 * `body`/`attachment` (get-messages lo hace, send/forward nunca porque el
 * mensaje recien creado nunca esta deleted).
 */
export function messageSnapshotToDto(m: MessageSnapshot): MessageDto {
  return {
    id: m.id,
    conversationId: m.conversationId,
    senderId: m.senderId,
    senderDisplayName: m.senderDisplayName,
    kind: m.kind,
    body: m.body,
    attachmentFileId: m.attachmentFileId,
    replyToMessageId: m.replyToMessageId,
    forwardedFromMessageId: m.forwardedFromMessageId,
    isEdited: m.isEdited,
    isDeleted: m.isDeleted,
    isPinned: m.isPinned,
    pinnedAtUtc: m.pinnedAtUtc ? m.pinnedAtUtc.toISOString() : null,
    pinnedByUserId: m.pinnedByUserId,
    createdAtUtc: m.createdAtUtc.toISOString(),
    editedAtUtc: m.editedAtUtc ? m.editedAtUtc.toISOString() : null,
  };
}
