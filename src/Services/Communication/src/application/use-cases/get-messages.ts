import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { MessageReactionSummaryDto, MessageDto } from '../../contracts/socket/chat-socket-events.js';
import { parseWaveform } from './chat-mappers.js';
import { resolveAvatars, type AvatarResolver } from './resolve-avatars.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { MessageEditPolicy } from '../../domain/conversations/message.js';
import { editPolicyFor } from './edit-message.js';

/**
 * Paginacion cursor-based con dos modos mutuamente excluyentes:
 *   - `beforeUtc`: scrollback hacia atras, desc por createdAt (uso normal al
 *     abrir la conversacion / cargar mas historial).
 *   - `afterUtc`: backfill hacia adelante, asc por createdAt — el cliente
 *     reconecta un socket tras estar offline y pide todo lo que se perdio
 *     desde el `createdAtUtc` de su ultimo mensaje conocido.
 * Si se mandan ambos, `afterUtc` gana (es el caso de uso mas especifico).
 */
export interface GetMessagesQuery {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly requesterUserId: string;
  readonly beforeUtc?: string;
  readonly afterUtc?: string;
  readonly take: number;
}

export interface GetMessagesResult {
  readonly items: readonly MessageDto[];
  readonly nextBeforeUtc: string | null;
  readonly nextAfterUtc: string | null;
  /** F8: ventana de edición de este chat (minutos desde el envío) y tope; el front oculta "Edit" fuera de ella. */
  readonly editPolicy?: MessageEditPolicy;
  /**
   * F8a: de qué es este chat. El deep link `/chat?conversation=` lleva Support y Meeting a su módulo, y un chat
   * archivado (meeting terminado, ticket cerrado) se abre con el composer apagado.
   */
  readonly conversation: {
    readonly id: string;
    readonly kind: 'Direct' | 'Group' | 'Support' | 'Meeting';
    readonly isArchived: boolean;
    readonly meetingId: string | null;
    readonly supportTicketId: string | null;
  };
  /** F8d (D-24, T-24): reacciones (tope por persona y catálogo curado). */
  readonly reactionPolicy?: { readonly maxPerUser: number; readonly catalog: readonly string[] };
}

export async function getMessages(
  query: GetMessagesQuery,
  deps: {
    conversations: ConversationRepository;
    messages: MessageRepository;
    avatars?: AvatarResolver;
    effectiveSettings?: Pick<EffectiveSettingsResolver, 'resolve'>;
  },
): Promise<Result<GetMessagesResult>> {
  const take = Math.min(Math.max(query.take, 1), 100);
  const conversation = await deps.conversations.findById(query.tenantId, query.conversationId, 0);
  if (!conversation) {
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }
  if (!conversation.isParticipant(query.requesterUserId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }

  const afterUtc = query.afterUtc ? new Date(query.afterUtc) : undefined;
  const beforeUtc = !afterUtc && query.beforeUtc ? new Date(query.beforeUtc) : undefined;
  const listArgs: {
    tenantId: string;
    conversationId: string;
    take: number;
    beforeUtc?: Date;
    afterUtc?: Date;
    notBeforeUtc?: Date | null;
  } = {
    tenantId: query.tenantId,
    conversationId: query.conversationId,
    take,
    // F8b (D-21): el cliente de un grupo ve desde su ingreso; el staff ve todo.
    notBeforeUtc: conversation.historyVisibleFrom(query.requesterUserId),
  };
  if (afterUtc !== undefined) listArgs.afterUtc = afterUtc;
  else if (beforeUtc !== undefined) listArgs.beforeUtc = beforeUtc;
  const messages = await deps.conversations.listMessages(listArgs);

  // Cotejos del historial: solo para los mensajes PROPIOS del solicitante, se consulta el
  // estado agregado del/los otro(s) participante(s) (entregado/leído). Así al abrir el chat el
  // emisor ve 1/2-grises/2-azules sin esperar eventos en vivo. Los ajenos van con null.
  const ownMessageIds = messages.filter((m) => m.senderId === query.requesterUserId && !m.isDeleted).map((m) => m.id);
  const receipts = await deps.messages.receiptsForOwnMessages({
    tenantId: query.tenantId,
    ownerUserId: query.requesterUserId,
    messageIds: ownMessageIds,
  });

  // F5: foto de los remitentes en una sola consulta (incluye a quien ya salio del grupo).
  const avatars = await resolveAvatars(
    deps.avatars,
    messages.map((m) => m.senderId),
  );

  // F8d (T-26): reacciones de la página en una sola consulta, agregadas por emoji.
  const reactions = deps.messages.listReactionsByMessages
    ? summarizeReactions(await deps.messages.listReactionsByMessages(query.tenantId, messages.filter((m) => !m.isDeleted).map((m) => m.id)))
    : null;

  const items: MessageDto[] = messages.map((m) => {
    const own = m.senderId === query.requesterUserId ? receipts.get(m.id) : undefined;
    return {
      id: m.id,
      conversationId: m.conversationId,
      senderId: m.senderId,
      senderDisplayName: m.senderDisplayName,
      ...(avatars ? { senderAvatarFileId: avatars.of(m.senderId) } : {}),
      kind: m.kind,
      body: m.isDeleted ? null : m.body,
      attachmentFileId: m.isDeleted ? null : m.attachmentFileId,
      replyToMessageId: m.replyToMessageId,
      forwardedFromMessageId: m.forwardedFromMessageId,
      isEdited: m.isEdited,
      editCount: m.editCount ?? 0,
      isDeleted: m.isDeleted,
      isPinned: m.isPinned,
      pinnedAtUtc: m.pinnedAtUtc ? m.pinnedAtUtc.toISOString() : null,
      pinnedByUserId: m.pinnedByUserId,
      createdAtUtc: m.createdAtUtc.toISOString(),
      editedAtUtc: m.editedAtUtc ? m.editedAtUtc.toISOString() : null,
      deliveredAtUtc: own?.deliveredAtUtc ? own.deliveredAtUtc.toISOString() : null,
      readAtUtc: own?.readAtUtc ? own.readAtUtc.toISOString() : null,
      // Nota de voz: la metadata de audio no se redacta al borrar (el player ya no la usa si isDeleted).
      audioDurationMs: m.audioDurationMs,
      audioWaveform: parseWaveform(m.audioWaveform),
      // F7 (D-27): estado y texto del transcript de la nota de voz (sin esto el hilo pierde el boton y el texto al recargar).
      ...(m.transcriptState !== undefined ? { transcriptState: m.isDeleted ? null : (m.transcriptState ?? null) } : {}),
      ...(m.transcriptText !== undefined ? { transcriptText: m.isDeleted ? null : (m.transcriptText ?? null) } : {}),
      ...(reactions ? { reactions: reactions.get(m.id) ?? [] } : {}),
    };
  });

  const hasMore = items.length === take;
  const lastItem = items[items.length - 1];
  const nextBeforeUtc = !afterUtc && hasMore && lastItem ? lastItem.createdAtUtc : null;
  const nextAfterUtc = afterUtc && hasMore && lastItem ? lastItem.createdAtUtc : null;

  const effective = deps.effectiveSettings ? await deps.effectiveSettings.resolve(query.tenantId) : null;
  const editPolicy = effective ? editPolicyFor(effective.chat.edit, conversation.kind) : undefined;
  // F8d (D-24, T-24): tope de emojis por persona y catálogo curado compartido (el mismo de la sala).
  const reactionPolicy = effective
    ? { maxPerUser: effective.chat.maxReactionsPerMessage, catalog: [...effective.media.reactions.catalog] }
    : undefined;
  return Result.ok({
    items,
    nextBeforeUtc,
    nextAfterUtc,
    ...(editPolicy ? { editPolicy } : {}),
    ...(reactionPolicy ? { reactionPolicy } : {}),
    conversation: {
      id: conversation.id,
      kind: conversation.kind,
      isArchived: conversation.isArchived,
      meetingId: conversation.meetingId,
      supportTicketId: conversation.supportTicketId,
    },
  });
}

/** Reacciones → por mensaje y emoji (en orden de primera aparición), con quién reaccionó. */
export function summarizeReactions(
  rows: ReadonlyArray<{ messageId: string; emoji: string; userId: string }>,
): Map<string, MessageReactionSummaryDto[]> {
  const byMessage = new Map<string, Map<string, MessageReactionSummaryDto>>();
  for (const row of rows) {
    const emojis = byMessage.get(row.messageId) ?? new Map<string, MessageReactionSummaryDto>();
    const entry = emojis.get(row.emoji) ?? { emoji: row.emoji, count: 0, userIds: [] };
    entry.count += 1;
    entry.userIds.push(row.userId);
    emojis.set(row.emoji, entry);
    byMessage.set(row.messageId, emojis);
  }
  return new Map([...byMessage].map(([id, emojis]) => [id, [...emojis.values()]]));
}
