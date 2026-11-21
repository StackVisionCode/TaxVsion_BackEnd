import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { sendMessage } from '../../src/application/use-cases/send-message.js';
import type { Conversation } from '../../src/domain/conversations/conversation.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { FileSourceDeclaredEvent } from '../../src/contracts/events/cloudstorage-events.js';
import { conversationWith, type ParticipantSeed } from '../helpers/chat-file-library.js';

const u = () => randomUUID();
const DECLARED = 'cloudstorage.file_source_declared.v1';

function setup(kind: 'Direct' | 'Group', others: ParticipantSeed[]) {
  const tenantId = u();
  const senderId = u();
  const conversation = conversationWith({
    tenantId,
    kind,
    participants: [{ userId: senderId, displayName: 'Ana' }, ...others],
  });
  const published: IntegrationEvent[] = [];
  const deps = {
    conversations: {
      findById: async (t: string, id: string) =>
        t === tenantId && id === conversation.id ? conversation : null,
      save: async (_c: Conversation) => undefined,
    } as unknown as ConversationRepository,
    idempotency: {
      tryReserve: async () => ({ status: 'fresh' as const, token: u() }),
      commit: async () => undefined,
      release: async () => undefined,
    },
    publisher: { enqueue: async (e: IntegrationEvent) => void published.push(e) },
    settings: {
      get: async (t: string) => ({
        tenantId: t,
        chatEnabled: true,
        employeeToEmployeeChatEnabled: true,
        restrictCustomerChatToAssignedPreparer: false,
        screenshotsEnabled: true,
      }),
    },
    attachmentTracking: {
      register: async () => undefined,
      markStatus: async () => null,
      findByFileId: async () => null,
    },
    attachmentReservations: {
      reserve: async () => undefined,
      consume: async () => true,
      purgeOlderThan: async () => 0,
    },
  };
  const send = (extra: { attachmentFileId?: string; body?: string; audioDurationMs?: number }) =>
    sendMessage(
      {
        tenantId,
        correlationId: 'corr',
        clientKey: u(),
        conversationId: conversation.id,
        senderUserId: senderId,
        ...extra,
      },
      deps,
    );
  const declarations = () => published.filter((e): e is FileSourceDeclaredEvent => e.eventType === DECLARED);
  return { tenantId, conversation, send, declarations };
}

describe('sendMessage — declara el origen del adjunto en CloudStorage', () => {
  it('un adjunto se declara conversation · attachment con el cliente del Direct', async () => {
    const customerId = u();
    const { tenantId, conversation, send, declarations } = setup('Direct', [
      { userId: u(), actorType: 'CustomerPortal', customerId },
    ]);
    const fileId = u();

    const result = await send({ attachmentFileId: fileId });

    expect(result.isSuccess).toBe(true);
    expect(declarations()).toEqual([
      expect.objectContaining({
        tenantId,
        correlationId: 'corr',
        fileId,
        sourceService: 'communication',
        sourceResourceType: 'conversation',
        sourceResourceId: conversation.id,
        sourceRole: 'attachment',
        relatedCustomerId: customerId,
      }),
    ]);
  });

  it('una nota de voz se declara como voice_note', async () => {
    const { send, declarations } = setup('Direct', [{ userId: u() }]);

    await send({ attachmentFileId: u(), audioDurationMs: 4_000 });

    expect(declarations().map((d) => [d.sourceRole, d.relatedCustomerId])).toEqual([['voice_note', null]]);
  });

  it('un texto no declara nada', async () => {
    const { send, declarations } = setup('Direct', [{ userId: u() }]);

    await send({ body: 'hola' });

    expect(declarations()).toEqual([]);
  });

  it('un grupo con dos clientes declara sin cliente relacionado', async () => {
    const { send, declarations } = setup('Group', [
      { userId: u(), actorType: 'CustomerPortal', customerId: u() },
      { userId: u(), actorType: 'CustomerPortal', customerId: u() },
    ]);

    await send({ attachmentFileId: u() });

    expect(declarations().map((d) => d.relatedCustomerId)).toEqual([null]);
  });
});
