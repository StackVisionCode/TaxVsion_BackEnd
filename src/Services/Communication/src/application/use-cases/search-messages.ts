import { Result, makeError } from '../../domain/shared/result.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageDto } from '../../contracts/socket/chat-socket-events.js';
import { messageSnapshotToDto } from './chat-mappers.js';

const MIN_QUERY_LENGTH = 2;
const MAX_QUERY_LENGTH = 200;
const DEFAULT_LIMIT = 50;
const MAX_LIMIT = 200;

export interface SearchMessagesCommand {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly actorUserId: string;
  readonly query: string;
  readonly limit?: number;
}

export interface SearchMessagesResult {
  readonly items: readonly MessageDto[];
  readonly limit: number;
  readonly truncated: boolean;
}

export interface SearchMessagesDeps {
  readonly messages: MessageRepository;
  readonly conversations: ConversationRepository;
}

/**
 * Fase Backend 9 — LIKE-based por Message.Body con filtro estricto de tenant
 * y conversation. NO usa CONTAINS/FREETEXT (requiere Full-Text catalog en el
 * DB, no configurado en el entorno actual). Limitacion documentada:
 *   - `LIKE '%q%'` NO es sargable → scan de indice al rango del ConversationId.
 *     Aceptable hasta ~100k mensajes por conversacion; conversations mas
 *     grandes deberian activar el catalog Full-Text y reemplazar esto por
 *     `Body CONTAINS (:q)` sin cambiar el port.
 *   - Case-insensitive por default (el collation del DB tipicamente es CI_AI).
 *   - No ordena por relevancia — devuelve por `CreatedAtUtc DESC` (mas recientes
 *     primero, comportamiento tipico de "buscar en el historial").
 *
 * `truncated` = el limite se alcanzo, puede haber mas mensajes matcheantes.
 */
export async function searchMessages(
  cmd: SearchMessagesCommand,
  deps: SearchMessagesDeps,
): Promise<Result<SearchMessagesResult>> {
  const query = cmd.query.trim();
  if (query.length < MIN_QUERY_LENGTH) {
    return Result.fail(makeError('Chat.Search.QueryTooShort', `Query must be at least ${MIN_QUERY_LENGTH} chars.`));
  }
  if (query.length > MAX_QUERY_LENGTH) {
    return Result.fail(makeError('Chat.Search.QueryTooLong', `Query must be at most ${MAX_QUERY_LENGTH} chars.`));
  }

  const conversation = await deps.conversations.findById(cmd.tenantId, cmd.conversationId);
  if (!conversation) return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  if (!conversation.isParticipant(cmd.actorUserId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }

  const limit = Math.max(1, Math.min(cmd.limit ?? DEFAULT_LIMIT, MAX_LIMIT));
  const rows = await deps.messages.searchByBody({
    tenantId: cmd.tenantId,
    conversationId: cmd.conversationId,
    query,
    limit,
  });

  return Result.ok({
    items: rows.map(messageSnapshotToDto),
    limit,
    truncated: rows.length === limit,
  });
}
