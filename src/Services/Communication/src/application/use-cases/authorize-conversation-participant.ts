import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';

/**
 * Verifica que el caller sea participante activo de la conversacion. Base de la
 * mediacion de subida de adjuntos (aun no hay AttachmentTracking en ese punto:
 * el fileId se registra al enviar el mensaje).
 */
export interface AuthorizeConversationParticipantCommand {
  readonly tenantId: string;
  readonly userId: string;
  readonly conversationId: string;
}

export interface AuthorizeConversationParticipantDeps {
  readonly conversations: ConversationRepository;
}

export async function authorizeConversationParticipant(
  command: AuthorizeConversationParticipantCommand,
  deps: AuthorizeConversationParticipantDeps,
): Promise<Result<void>> {
  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId, 0);
  if (!conversation) {
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }
  if (!conversation.isParticipant(command.userId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }
  return Result.okVoid();
}
