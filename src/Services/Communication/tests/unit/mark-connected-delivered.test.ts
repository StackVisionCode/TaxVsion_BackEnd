import { describe, it, expect } from 'vitest';
import { markConnectedDelivered } from '../../src/application/use-cases/mark-connected-delivered.js';
import type { MessageRepository } from '../../src/application/ports/message-repository.js';

/**
 * `markConnectedDelivered` hace cumplir "conectado = entregado (2 grises)" para el backlog:
 * al conectar, marca delivered los mensajes entrantes sin receipt y devuelve un receipt por
 * conversación con marcas nuevas, para que el handler los emita al emisor.
 */
function repoReturning(
  marks: { conversationId: string; upToMessageId: string; markedCount: number }[],
  spy?: (input: unknown) => void,
): MessageRepository {
  return {
    async markPendingDeliveredForConversations(input) {
      spy?.(input);
      return marks;
    },
  } as unknown as MessageRepository;
}

describe('markConnectedDelivered', () => {
  it('devuelve [] sin llamar al repo cuando no hay conversaciones', async () => {
    let called = false;
    const messages = repoReturning([], () => {
      called = true;
    });
    const receipts = await markConnectedDelivered(
      { tenantId: 't1', userUserId: 'u1', conversationIds: [] },
      { messages },
    );
    expect(receipts).toEqual([]);
    expect(called).toBe(false);
  });

  it('mapea cada conversación con marcas nuevas a un DeliveryReceiptDto del receptor', async () => {
    const messages = repoReturning([
      { conversationId: 'c1', upToMessageId: 'm9', markedCount: 3 },
      { conversationId: 'c2', upToMessageId: 'm4', markedCount: 1 },
    ]);
    const receipts = await markConnectedDelivered(
      { tenantId: 't1', userUserId: 'u1', conversationIds: ['c1', 'c2'] },
      { messages },
    );
    expect(receipts).toHaveLength(2);
    expect(receipts[0]).toMatchObject({ conversationId: 'c1', userId: 'u1', upToMessageId: 'm9' });
    expect(receipts[1]).toMatchObject({ conversationId: 'c2', userId: 'u1', upToMessageId: 'm4' });
    // deliveredAtUtc es un ISO string válido.
    expect(new Date(receipts[0].deliveredAtUtc).toISOString()).toBe(receipts[0].deliveredAtUtc);
  });

  it('no emite receipts para conversaciones sin marcas nuevas (backlog ya entregado)', async () => {
    const messages = repoReturning([]);
    const receipts = await markConnectedDelivered(
      { tenantId: 't1', userUserId: 'u1', conversationIds: ['c1', 'c2'] },
      { messages },
    );
    expect(receipts).toEqual([]);
  });
});
