import { Result, makeError } from '../../domain/shared/result.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { MessageEditedDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Comando: editar el body de un mensaje Text. Solo el sender puede editar.
 * Idempotente por (tenantId, senderId, clientKey) — reintentos del cliente no
 * agregan `IsEdited`. El aggregate valida las reglas; el use case solo carga
 * y persiste.
 */
export interface EditMessageCommand {
  readonly tenantId: string;
  readonly clientKey: string;
  readonly messageId: string;
  readonly senderUserId: string;
  readonly body: string;
}

export interface EditMessageResult {
  readonly edited: MessageEditedDto;
}

export interface EditMessageDeps {
  readonly messages: MessageRepository;
  readonly idempotency: IdempotencyStore;
}

export async function editMessage(
  command: EditMessageCommand,
  deps: EditMessageDeps,
): Promise<Result<EditMessageResult>> {
  const reservation = await deps.idempotency.tryReserve<EditMessageResult>({
    tenantId: command.tenantId,
    userId: command.senderUserId,
    scope: 'chat.message.edit',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const message = await deps.messages.findById(command.tenantId, command.messageId);
  if (!message) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.senderUserId,
      scope: 'chat.message.edit',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(makeError('Chat.Message.NotFound', 'Message not found.'));
  }

  const now = new Date();
  const editResult = message.editText(command.body, command.senderUserId, now);
  if (!editResult.isSuccess) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.senderUserId,
      scope: 'chat.message.edit',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(editResult.error);
  }
  await deps.messages.update(command.tenantId, message);

  const snapshot = message.toSnapshot();
  const dto: MessageEditedDto = {
    messageId: snapshot.id,
    conversationId: snapshot.conversationId,
    body: snapshot.body ?? '',
    editedAtUtc: (snapshot.editedAtUtc ?? now).toISOString(),
  };
  const result: EditMessageResult = { edited: dto };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.senderUserId,
    scope: 'chat.message.edit',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
