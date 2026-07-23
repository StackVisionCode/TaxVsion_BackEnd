import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { checkPermission, CommunicationPermissions } from '../../domain/shared/permissions.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import {
  ChatSocketEvents,
  type MessagePinnedDto,
} from '../../contracts/socket/chat-socket-events.js';

export interface PinMessageCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly messageId: string;
  readonly actorUserId: string;
  readonly actorType: string;
  readonly actorPermissionVersion: number;
}

export interface PinMessageDeps {
  readonly messages: MessageRepository;
  readonly conversations: ConversationRepository;
  readonly emitter: RealtimeEmitter;
  readonly userPermissions: UserPermissionsProjectionRepository;
}

/**
 * Politica de autorizacion:
 *   - Direct + Support (2-party): cualquier participante puede pinear.
 *   - Group + Meeting: requiere `ChatModerate` (moderators tipicos) o rol
 *     TenantAdmin/PlatformAdmin (via hasPermission auto-pass).
 *
 * Idempotente: pin sobre un mensaje ya pineado retorna ok sin re-emitir.
 */
export async function pinMessage(
  cmd: PinMessageCommand,
  deps: PinMessageDeps,
): Promise<Result<{ wasNew: boolean }>> {
  const message = await deps.messages.findById(cmd.tenantId, cmd.messageId);
  if (!message) return Result.fail(makeError('Chat.Message.NotFound', 'Message not found.'));

  const snapshot = message.toSnapshot();
  if (snapshot.isDeleted) {
    return Result.fail(makeError('Chat.Pin.MessageDeleted', 'Cannot pin a deleted message.'));
  }

  const conversation = await deps.conversations.findById(cmd.tenantId, snapshot.conversationId);
  if (!conversation) return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  if (!conversation.isParticipant(cmd.actorUserId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }

  const requiresModerate = conversation.kind === 'Group' || conversation.kind === 'Meeting';
  if (requiresModerate) {
    const permCheck = await checkPermission(
      {
        userId: cmd.actorUserId,
        actorType: cmd.actorType,
        permissionVersion: cmd.actorPermissionVersion,
      },
      CommunicationPermissions.ChatModerate,
      deps.userPermissions,
    );
    if (!permCheck.allowed) {
      return Result.fail(makeError(permCheck.code, permCheck.message));
    }
  }

  if (snapshot.isPinned) return Result.ok({ wasNew: false });

  const now = new Date();
  message.pin(cmd.actorUserId, now);
  await deps.messages.update(cmd.tenantId, message);

  const dto: MessagePinnedDto = {
    messageId: cmd.messageId,
    conversationId: snapshot.conversationId,
    pinnedByUserId: cmd.actorUserId,
    pinnedAtUtc: now.toISOString(),
  };
  deps.emitter.emitToConversation({
    tenantId: cmd.tenantId,
    conversationId: snapshot.conversationId,
    event: ChatSocketEvents.MessagePinned,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: now.toISOString(),
      payload: dto,
    },
  });
  return Result.ok({ wasNew: true });
}
