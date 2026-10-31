import { Result, makeError } from '../../domain/shared/result.js';
import { computeMeetingUniquenessKey } from '../../domain/conversations/uniqueness-key.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageDto } from '../../contracts/socket/chat-socket-events.js';
import { messageSnapshotToDto } from './chat-mappers.js';
import { resolveAvatars, type AvatarResolver } from './resolve-avatars.js';

export interface GetMeetingChatHistoryQuery {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  readonly beforeUtc?: string;
  readonly take: number;
}

export interface MeetingChatHistoryResult {
  readonly conversationId: string | null;
  readonly isArchived: boolean;
  readonly items: readonly MessageDto[];
  readonly nextBeforeUtc: string | null;
}

/**
 * F8a (D-01): el chat de la sala se lee en la ficha del meeting, en solo lectura, por todos los que
 * participaron (aunque hayan salido antes). Nunca aparece en la lista de Chat. Mismo permiso que la ficha.
 */
export async function getMeetingChatHistory(
  query: GetMeetingChatHistoryQuery,
  deps: {
    meetings: Pick<MeetingRepository, 'findById'>;
    conversations: Pick<ConversationRepository, 'findByUniquenessKey' | 'listMessages'>;
    avatars?: AvatarResolver;
  },
): Promise<Result<MeetingChatHistoryResult>> {
  const meeting = await deps.meetings.findById(query.tenantId, query.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const snapshot = meeting.toSnapshot();
  const canRead = query.userId === snapshot.hostUserId || meeting.isCohost(query.userId) || meeting.isKnownParticipant(query.userId);
  if (!canRead) return Result.fail(makeError('Meeting.NotParticipant', 'Only participants can read the meeting chat.'));

  const conversation = await deps.conversations.findByUniquenessKey(query.tenantId, computeMeetingUniquenessKey(query.meetingId));
  if (!conversation) return Result.ok({ conversationId: null, isArchived: true, items: [], nextBeforeUtc: null });

  const take = Math.min(Math.max(query.take, 1), 100);
  const messages = await deps.conversations.listMessages({
    tenantId: query.tenantId,
    conversationId: conversation.id,
    take,
    ...(query.beforeUtc ? { beforeUtc: new Date(query.beforeUtc) } : {}),
  });
  const avatars = await resolveAvatars(deps.avatars, messages.map((m) => m.senderId));
  const items = messages.map((m) => ({
    ...messageSnapshotToDto(m),
    ...(avatars ? { senderAvatarFileId: avatars.of(m.senderId) } : {}),
  }));
  const oldest = messages[messages.length - 1];
  return Result.ok({
    conversationId: conversation.id,
    isArchived: conversation.isArchived,
    items,
    nextBeforeUtc: messages.length === take && oldest ? oldest.createdAtUtc.toISOString() : null,
  });
}
