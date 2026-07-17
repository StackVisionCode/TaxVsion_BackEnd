import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';

/**
 * Reaction a un Message (Fase Backend 9). Entidad hija NO administrada por el
 * aggregate Conversation — el volumen esperado (N-usuarios x M-emojis por
 * mensaje) hace impractico rehidratarlas junto con el mensaje. Se accede via
 * MessageRepository.addReaction / removeReaction / listByMessage.
 *
 * El unique (MessageId, UserId, Emoji) en Prisma bloquea duplicados a nivel
 * BD; este aggregate solo se encarga del emoji-shape (evitar strings vacios,
 * limitar largo — no queremos que el "emoji" sea un parrafo de texto).
 */
const EMOJI_MAX_LENGTH = 16;

export interface MessageReactionSnapshot {
  readonly id: string;
  readonly messageId: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly emoji: string;
  readonly createdAtUtc: Date;
}

export class MessageReaction {
  private constructor(private state: MessageReactionSnapshot) {}

  static rehydrate(snapshot: MessageReactionSnapshot): MessageReaction {
    return new MessageReaction(snapshot);
  }

  static create(input: {
    messageId: string;
    tenantId: string;
    userId: string;
    emoji: string;
    now?: Date;
  }): Result<MessageReaction> {
    const trimmed = input.emoji.trim();
    if (trimmed.length === 0) {
      return Result.fail(makeError('Chat.Reaction.EmptyEmoji', 'Emoji is required.'));
    }
    if (trimmed.length > EMOJI_MAX_LENGTH) {
      return Result.fail(
        makeError(
          'Chat.Reaction.EmojiTooLong',
          `Emoji must be at most ${EMOJI_MAX_LENGTH} chars — supports variation selectors + skin tones, no more.`,
        ),
      );
    }
    return Result.ok(
      new MessageReaction({
        id: randomUUID(),
        messageId: input.messageId,
        tenantId: input.tenantId,
        userId: input.userId,
        emoji: trimmed,
        createdAtUtc: input.now ?? new Date(),
      }),
    );
  }

  toSnapshot(): MessageReactionSnapshot {
    return this.state;
  }

  get emoji(): string {
    return this.state.emoji;
  }
}
