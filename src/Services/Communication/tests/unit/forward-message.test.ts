import { describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { forwardMessage, type ForwardMessageDeps } from '../../src/application/use-cases/forward-message.js';

const u = (): string => randomUUID();

function direct(tenantId: string, a: string, b: string): Conversation {
  const r = Conversation.startDirect({
    tenantId,
    initiator: { userId: a, displayName: 'A', actorType: 'TenantEmployee' },
    recipient: { userId: b, displayName: 'B', actorType: 'TenantEmployee' },
  });
  if (!r.isSuccess) throw new Error(r.error.code);
  return r.value;
}

/** F11 (E-259): reenviar exige poder leer el mensaje de origen (antes bastaba con conocer su id). */
describe('forwardMessage', () => {
  function setup() {
    const tenantId = u();
    const [ana, bo, eve] = [u(), u(), u()];
    const origin = direct(tenantId, ana, bo); // Eve no está aquí
    const sent = origin.sendText({ senderId: ana, body: 'W-2 attached' });
    if (!sent.isSuccess) throw new Error(sent.error.code);
    const target = direct(tenantId, eve, u());
    const byId = new Map([
      [origin.id, origin],
      [target.id, target],
    ]);
    const insertForwarded = vi.fn().mockResolvedValue(undefined);
    const deps = {
      messages: { findById: vi.fn(async () => sent.value), insertForwarded },
      conversations: { findById: vi.fn(async (_t: string, id: string) => byId.get(id) ?? null) },
      idempotency: {
        tryReserve: vi.fn(async () => ({ status: 'reserved', token: 't' })),
        release: vi.fn(async () => undefined),
        commit: vi.fn(async () => undefined),
      },
      emitter: { emitToConversation: vi.fn() },
      userDirectory: { findByUserId: vi.fn(async () => null) },
    } as unknown as ForwardMessageDeps;
    return { tenantId, ana, eve, origin, target, sent: sent.value, deps, insertForwarded };
  }

  it('quien no participa del chat de origen no puede reenviar su mensaje (NotFound, nada se inserta)', async () => {
    const { tenantId, eve, target, sent, deps, insertForwarded } = setup();
    const result = await forwardMessage(
      { tenantId, correlationId: u(), clientKey: u(), originMessageId: sent.id, targetConversationId: target.id, forwarderUserId: eve },
      deps,
    );
    expect(result.isSuccess ? 'ok' : result.error.code).toBe('Chat.Message.NotFound');
    expect(insertForwarded).not.toHaveBeenCalled();
  });
});
