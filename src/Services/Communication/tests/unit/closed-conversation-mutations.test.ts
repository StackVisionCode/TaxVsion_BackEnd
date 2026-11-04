import { randomUUID } from 'node:crypto';
import { describe, expect, it, vi } from 'vitest';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import type { Message } from '../../src/domain/conversations/message.js';
import { addMessageReaction } from '../../src/application/use-cases/add-message-reaction.js';
import { removeMessageReaction } from '../../src/application/use-cases/remove-message-reaction.js';
import { pinMessage } from '../../src/application/use-cases/pin-message.js';
import { unpinMessage } from '../../src/application/use-cases/unpin-message.js';
import { deleteMessage, type DeleteMessageDeps } from '../../src/application/use-cases/delete-message.js';

const u = (): string => randomUUID();
const code = (r: { isSuccess: boolean; error?: { code: string } }): string => (r.isSuccess ? 'ok' : (r.error?.code ?? '?'));

function direct(tenantId: string, a: string, b: string): Conversation {
  const r = Conversation.startDirect({
    tenantId,
    initiator: { userId: a, displayName: 'Ana', actorType: 'TenantEmployee' },
    recipient: { userId: b, displayName: 'Leo', actorType: 'TenantEmployee' },
  });
  if (!r.isSuccess) throw new Error(r.error.code);
  return r.value;
}

/** Chat cerrado (archivado o Direct con el otro retirado): el backend rechaza reaccionar, fijar y borrar. */
describe('mutaciones en un chat cerrado', () => {
  function setup(close: 'left' | 'archived' | 'open') {
    const tenantId = u();
    const [ana, leo] = [u(), u()];
    const conversation = direct(tenantId, ana, leo);
    const sent = conversation.sendText({ senderId: ana, body: 'W-2 attached' });
    if (!sent.isSuccess) throw new Error(sent.error.code);
    const message: Message = sent.value;
    message.pin(ana);
    if (close === 'left') conversation.retireParticipantBySystem(leo);
    if (close === 'archived') conversation.archive();
    const messages = {
      findById: vi.fn(async () => message),
      update: vi.fn(async () => undefined),
      addReaction: vi.fn(async () => ({ wasNew: true })),
      removeReaction: vi.fn(async () => ({ wasPresent: true })),
      listReactionsByMessage: vi.fn(async () => []),
    };
    const idempotency = {
      tryReserve: vi.fn(async () => ({ status: 'reserved', token: 't' })),
      release: vi.fn(async () => undefined),
      commit: vi.fn(async () => undefined),
    };
    const deps = {
      messages,
      conversations: { findById: vi.fn(async () => conversation) },
      emitter: { emitToConversation: vi.fn() },
      userPermissions: {},
      idempotency,
      publisher: { enqueue: vi.fn(async () => undefined) },
    };
    const base = { tenantId, correlationId: 'c', messageId: message.id };
    return { ana, leo, message, messages, idempotency, deps, base };
  }

  it('Direct con el otro retirado: reaccionar y quitar reacción dan ParticipantLeft y no tocan nada', async () => {
    const { ana, messages, deps, base } = setup('left');
    expect(code(await addMessageReaction({ ...base, userId: ana, emoji: '👍' }, deps as never))).toBe('Chat.Conversation.ParticipantLeft');
    expect(code(await removeMessageReaction({ ...base, userId: ana, emoji: '👍' }, deps as never))).toBe('Chat.Conversation.ParticipantLeft');
    expect(messages.addReaction).not.toHaveBeenCalled();
    expect(messages.removeReaction).not.toHaveBeenCalled();
    expect(deps.emitter.emitToConversation).not.toHaveBeenCalled();
  });

  it('archivado: fijar y desfijar dan Archived y el mensaje no cambia', async () => {
    const { ana, message, messages, deps, base } = setup('archived');
    const actor = { ...base, actorUserId: ana, actorType: 'TenantEmployee', actorPermissionVersion: 1 };
    message.unpin();
    expect(code(await pinMessage(actor, deps as never))).toBe('Chat.Conversation.Archived');
    message.pin(ana);
    expect(code(await unpinMessage(actor, deps as never))).toBe('Chat.Conversation.Archived');
    expect(message.toSnapshot().isPinned).toBe(true);
    expect(messages.update).not.toHaveBeenCalled();
  });

  it('el autor no borra su mensaje en un chat cerrado (libera la reserva de idempotencia)', async () => {
    const { ana, message, messages, idempotency, deps, base } = setup('left');
    const result = await deleteMessage(
      { ...base, clientKey: u(), actorUserId: ana, actorCanModerate: false },
      deps as unknown as DeleteMessageDeps,
    );
    expect(code(result)).toBe('Chat.Conversation.ParticipantLeft');
    expect(message.toSnapshot().isDeleted).toBe(false);
    expect(messages.update).not.toHaveBeenCalled();
    expect(idempotency.release).toHaveBeenCalledOnce();
  });

  it('moderación del momento (host/cohost, Owner/Admin) no alcanza en un chat archivado', async () => {
    const { leo, deps, base } = setup('archived');
    const result = await deleteMessage(
      { ...base, clientKey: u(), actorUserId: leo, actorCanModerate: true, actorHasModeratePermission: false },
      deps as unknown as DeleteMessageDeps,
    );
    expect(code(result)).toBe('Chat.Conversation.Archived');
  });

  it('chat.moderate sí borra en un chat cerrado (retirar contenido indebido del historial)', async () => {
    const moderator = u();
    const { message, messages, deps, base } = setup('archived');
    const result = await deleteMessage(
      { ...base, clientKey: u(), actorUserId: moderator, actorCanModerate: false, actorHasModeratePermission: true },
      deps as unknown as DeleteMessageDeps,
    );
    expect(code(result)).toBe('ok');
    expect(message.toSnapshot().isDeleted).toBe(true);
    expect(messages.update).toHaveBeenCalledOnce();
  });

  it('abierto: todo sigue como antes', async () => {
    const { ana, leo, messages, deps, base } = setup('open');
    expect(code(await addMessageReaction({ ...base, userId: leo, emoji: '👍' }, deps as never))).toBe('ok');
    expect(code(await removeMessageReaction({ ...base, userId: leo, emoji: '👍' }, deps as never))).toBe('ok');
    const actor = { ...base, actorUserId: ana, actorType: 'TenantEmployee', actorPermissionVersion: 1 };
    expect(code(await unpinMessage(actor, deps as never))).toBe('ok');
    expect(code(await deleteMessage({ ...base, clientKey: u(), actorUserId: ana, actorCanModerate: false }, deps as unknown as DeleteMessageDeps))).toBe('ok');
    expect(messages.update).toHaveBeenCalledTimes(2);
  });
});
