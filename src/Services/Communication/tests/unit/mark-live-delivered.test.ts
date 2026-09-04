import { describe, it, expect } from 'vitest';
import { markLiveDelivered } from '../../src/application/use-cases/mark-live-delivered.js';
import type { MessageRepository } from '../../src/application/ports/message-repository.js';
import type { PresenceService } from '../../src/application/ports/presence-service.js';

/**
 * `markLiveDelivered` marca entregado un mensaje recién enviado SOLO a los destinatarios con sesión
 * viva (los online), y devuelve un receipt por cada uno para que el emisor pinte los 2 grises.
 */
function presenceOnline(online: readonly string[]): PresenceService {
  return {
    async listOnline(_tenantId: string, userIds: readonly string[]) {
      return userIds.filter((u) => online.includes(u));
    },
  } as unknown as PresenceService;
}

function recordingMessages(recorded: string[]): MessageRepository {
  return {
    async recordDelivered(input: { userId: string }) {
      recorded.push(input.userId);
    },
  } as unknown as MessageRepository;
}

describe('markLiveDelivered', () => {
  it('no hace nada sin destinatarios', async () => {
    const recorded: string[] = [];
    const receipts = await markLiveDelivered(
      { tenantId: 't1', conversationId: 'c1', messageId: 'm1', recipientUserIds: [] },
      { presence: presenceOnline(['u2']), messages: recordingMessages(recorded) },
    );
    expect(receipts).toEqual([]);
    expect(recorded).toEqual([]);
  });

  it('marca delivered y emite receipt SOLO para los destinatarios online', async () => {
    const recorded: string[] = [];
    const receipts = await markLiveDelivered(
      { tenantId: 't1', conversationId: 'c1', messageId: 'm9', recipientUserIds: ['u2', 'u3'] },
      { presence: presenceOnline(['u2']), messages: recordingMessages(recorded) },
    );
    expect(recorded).toEqual(['u2']); // u3 offline → no se marca
    expect(receipts).toHaveLength(1);
    const [only] = receipts;
    if (!only) throw new Error('se esperaba 1 receipt');
    expect(only).toMatchObject({ conversationId: 'c1', userId: 'u2', upToMessageId: 'm9' });
    expect(new Date(only.deliveredAtUtc).toISOString()).toBe(only.deliveredAtUtc);
  });

  it('no marca ni emite si ningún destinatario está online (queda en enviado)', async () => {
    const recorded: string[] = [];
    const receipts = await markLiveDelivered(
      { tenantId: 't1', conversationId: 'c1', messageId: 'm1', recipientUserIds: ['u2', 'u3'] },
      { presence: presenceOnline([]), messages: recordingMessages(recorded) },
    );
    expect(receipts).toEqual([]);
    expect(recorded).toEqual([]);
  });
});
