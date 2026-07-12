import { Result, makeError } from '../shared/result.js';

/**
 * Body de un mensaje Text. Cap 4000 chars (misma cota que el schema Prisma;
 * el legacy tenia 2000 en el FE y sin cota en el server — bug de fuera).
 * Trim conservador para evitar payload gigante con whitespace.
 */
const MAX_BODY_LENGTH = 4000;

export interface MessageBody {
  readonly value: string;
}

export function makeMessageBody(input: string | null | undefined): Result<MessageBody> {
  if (input === null || input === undefined) {
    return Result.fail(makeError('Chat.Message.EmptyBody', 'Message body is required for Text messages.'));
  }
  const trimmed = input.trim();
  if (trimmed.length === 0) {
    return Result.fail(makeError('Chat.Message.EmptyBody', 'Message body cannot be empty.'));
  }
  if (trimmed.length > MAX_BODY_LENGTH) {
    return Result.fail(
      makeError('Chat.Message.TooLong', `Message body exceeds ${MAX_BODY_LENGTH} chars.`),
    );
  }
  return Result.ok({ value: trimmed });
}
