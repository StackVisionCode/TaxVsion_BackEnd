import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { hasPermission, CommunicationPermissions } from '../../domain/shared/permissions.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  ChatSocketEvents,
  type MessageUnpinnedDto,
} from '../../contracts/socket/chat-socket-events.js';

export interface UnpinMessageCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly messageId: string;
  readonly actorUserId: string;
  readonly actorType: string;
  readonly actorPermissions: readonly string[];
}

export interface UnpinMessageDeps {
  readonly messages: MessageRepository;
  readonly conversations: ConversationRepository;
  readonly emitter: RealtimeEmitter;
}

export async function unpinMessage(
  cmd: UnpinMessageCommand,
  deps: UnpinMessageDeps,
): Promise<Result<{ wasPinned: boolean }>> {
  const message = await deps.messages.findById(cmd.tenantId, cmd.messageId);
  if (!message) return Result.fail(makeError('Chat.Message.NotFound', 'Message not found.'));

  const snapshot = message.toSnapshot();
  if (!snapshot.isPinned) return Result.ok({ wasPinned: false });

  const conversation = await deps.conversations.findById(cmd.tenantId, snapshot.conversationId);
  if (!conversation) return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  if (!conversation.isParticipant(cmd.actorUserId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }

  const requiresModerate = conversation.kind === 'Group' || conversation.kind === 'Meeting';
  if (requiresModerate) {
    if (!hasPermission(cmd.actorType, cmd.actorPermissions, CommunicationPermissions.ChatModerate)) {
      return Result.fail(makeError('Auth.Forbidden', 'Unpin in Group/Meeting requires communication.chat.moderate.'));
    }
  }

  const now = new Date();
  message.unpin(now);
  await deps.messages.update(cmd.tenantId, message);

  const dto: MessageUnpinnedDto = {
    messageId: cmd.messageId,
    conversationId: snapshot.conversationId,
    unpinnedByUserId: cmd.actorUserId,
    unpinnedAtUtc: now.toISOString(),
  };
  deps.emitter.emitToConversation({
    tenantId: cmd.tenantId,
    conversationId: snapshot.conversationId,
    event: ChatSocketEvents.MessageUnpinned,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: now.toISOString(),
      payload: dto,
    },
  });
  return Result.ok({ wasPinned: true });
}
