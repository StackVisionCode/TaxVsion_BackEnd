import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation, type ConversationSnapshot } from '../../src/domain/conversations/conversation.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { MessageRepository } from '../../src/application/ports/message-repository.js';
import type { MessageSnapshot } from '../../src/domain/conversations/message.js';
import type { IdempotencyStore, IdempotencyReservation } from '../../src/application/ports/idempotency-store.js';
import type { TenantSettingsProvider } from '../../src/application/ports/tenant-settings-provider.js';
import type { AttachmentTrackingRepository } from '../../src/application/ports/attachment-tracking-repository.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import { sendMessage } from '../../src/application/use-cases/send-message.js';
import { markMessagesRead } from '../../src/application/use-cases/mark-messages-read.js';
import { expectRejectedCrossTenant } from '../helpers/tenant-isolation.js';

function u(): string {
  return randomUUID();
}

/**
 * Fake tenant-scoped, igual criterio que PrismaConversationRepository
 * (`where: { Id, TenantId }`): una key que exige coincidencia exacta de
 * tenant es lo que hace que este test sea capaz de detectar una regresion
 * real (ej. alguien cambia `findById` para ignorar `tenantId`).
 */
class FakeConversationRepository implements ConversationRepository {
  private store = new Map<string, Conversation>();

  seed(tenantId: string, conversation: Conversation): void {
    this.store.set(`${tenantId}:${conversation.id}`, conversation);
  }

  async save(): Promise<void> {}

  async findById(tenantId: string, id: string): Promise<Conversation | null> {
    return this.store.get(`${tenantId}:${id}`) ?? null;
  }

  async findByUniquenessKey(): Promise<Conversation | null> {
    return null;
  }

  async listForUser(): Promise<ConversationSnapshot[]> {
    return [];
  }

  async countUnreadByKind(): Promise<{ Direct: number; Group: number; Support: number; Meeting: number }> {

    return { Direct: 0, Group: 0, Support: 0, Meeting: 0 };

  }

  async countForUser(): Promise<number> {
    return 0;
  }

  async listMessages(): Promise<MessageSnapshot[]> {
    return [];
  }

  async countUnreadForUser(): Promise<number> {
    return 0;
  }
}

class FakeMessageRepository implements MessageRepository {
  async findById() {
    return null;
  }
  async update(): Promise<void> {}
  async recordEdit(): Promise<void> {}
  async countAttachmentsBySender(): Promise<number> {
    return 0;
  }
  async listEdits(): Promise<[]> {
    return [];
  }
  async insertForwarded(): Promise<void> {}
  async markBatchRead(): Promise<{ markedCount: number }> {
    return { markedCount: 1 };
  }
  async recordDelivered(): Promise<void> {}
  async markBatchDelivered(): Promise<{ markedCount: number }> {
    return { markedCount: 1 };
  }
  async markPendingDeliveredForConversations(): Promise<
    { conversationId: string; upToMessageId: string; markedCount: number }[]
  > {
    return [];
  }
  async receiptsForOwnMessages(): Promise<Map<string, { deliveredAtUtc: Date | null; readAtUtc: Date | null }>> {
    return new Map();
  }
  async listByIds(): Promise<MessageSnapshot[]> {
    return [];
  }
  async addReaction() {
    return { wasNew: false };
  }
  async removeReaction() {
    return { wasPresent: false };
  }
  async listReactionsByMessage() {
    return [];
  }
  async searchByBody(): Promise<MessageSnapshot[]> {
    return [];
  }
}

class FakeIdempotencyStore implements IdempotencyStore {
  async tryReserve<T>(): Promise<IdempotencyReservation<T>> {
    return { status: 'fresh', token: u() };
  }
  async commit(): Promise<void> {}
  async release(): Promise<void> {}
}

class FakeTenantSettingsProvider implements TenantSettingsProvider {
  async get(tenantId: string) {
    return {
      tenantId,
      chatEnabled: true,
      employeeToEmployeeChatEnabled: true,
      restrictCustomerChatToAssignedPreparer: false,
      screenshotsEnabled: true,
      messageRetentionDays: 365,
    };
  }
}

class FakeAttachmentTrackingRepository implements AttachmentTrackingRepository {
  async register(): Promise<void> {}
  async markStatus() {
    return null;
  }
  async findByFileId() {
    return null;
  }
}

class FakeIntegrationEventPublisher implements IntegrationEventPublisher {
  readonly published: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.published.push(event);
  }
}

function seedDirectConversation(conversations: FakeConversationRepository, tenantId: string) {
  const initiatorId = u();
  const recipientId = u();
  const result = Conversation.startDirect({
    tenantId,
    initiator: { userId: initiatorId, displayName: 'Alice', actorType: 'TenantEmployee' },
    recipient: { userId: recipientId, displayName: 'Bob', actorType: 'TenantEmployee' },
  });
  if (!result.isSuccess) throw new Error('failed to seed conversation');
  conversations.seed(tenantId, result.value);
  return { conversationId: result.value.id, initiatorId, recipientId };
}

/** Reservas en memoria con la misma semántica que la tabla: consumir exige coincidencia total y es de un solo uso. */
function memoryReservations() {
  const rows = new Map<string, { tenantId: string; conversationId: string; uploadedByUserId: string }>();
  return {
    rows,
    async reserve(r: { fileId: string; tenantId: string; conversationId: string; uploadedByUserId: string }) {
      rows.set(r.fileId, { tenantId: r.tenantId, conversationId: r.conversationId, uploadedByUserId: r.uploadedByUserId });
    },
    async consume(r: { fileId: string; tenantId: string; conversationId: string; uploadedByUserId: string }) {
      const row = rows.get(r.fileId);
      const ok = !!row && row.tenantId === r.tenantId && row.conversationId === r.conversationId && row.uploadedByUserId === r.uploadedByUserId;
      if (ok) rows.delete(r.fileId);
      return ok;
    },
    async purgeOlderThan() {
      return 0;
    },
  };
}

describe('Adjuntos — solo los subidos por el emisor para ese chat, una vez (F11, E-259)', () => {
  function setup() {
    const tenantId = u();
    const conversations = new FakeConversationRepository();
    const { conversationId, initiatorId, recipientId } = seedDirectConversation(conversations, tenantId);
    const attachmentReservations = memoryReservations();
    const deps = {
      conversations,
      idempotency: new FakeIdempotencyStore(),
      publisher: new FakeIntegrationEventPublisher(),
      settings: new FakeTenantSettingsProvider(),
      attachmentTracking: new FakeAttachmentTrackingRepository(),
      attachmentReservations,
    };
    const send = (senderUserId: string, fileId: string, conversation = conversationId) =>
      sendMessage({ tenantId, correlationId: u(), clientKey: u(), conversationId: conversation, senderUserId, attachmentFileId: fileId }, deps);
    return { tenantId, conversationId, initiatorId, recipientId, attachmentReservations, send };
  }

  it('sin reserva (fileId ajeno o inventado) responde NotFound', async () => {
    const { initiatorId, send } = setup();
    const result = await send(initiatorId, u());
    expect(result.isSuccess ? 'ok' : result.error.code).toBe('Chat.Attachment.NotFound');
  });

  it('con la reserva del emisor se envía y la reserva se consume: el mismo archivo no se usa dos veces', async () => {
    const { tenantId, conversationId, initiatorId, attachmentReservations, send } = setup();
    const fileId = u();
    await attachmentReservations.reserve({ fileId, tenantId, conversationId, uploadedByUserId: initiatorId });
    expect((await send(initiatorId, fileId)).isSuccess).toBe(true);
    const again = await send(initiatorId, fileId);
    expect(again.isSuccess ? 'ok' : again.error.code).toBe('Chat.Attachment.NotFound');
  });

  it('otro participante no puede enviar un archivo que subió alguien más', async () => {
    const { tenantId, conversationId, initiatorId, recipientId, attachmentReservations, send } = setup();
    const fileId = u();
    await attachmentReservations.reserve({ fileId, tenantId, conversationId, uploadedByUserId: initiatorId });
    const result = await send(recipientId, fileId);
    expect(result.isSuccess ? 'ok' : result.error.code).toBe('Chat.Attachment.NotFound');
    expect(attachmentReservations.rows.has(fileId)).toBe(true);
  });
});

describe('Tenant isolation — sendMessage', () => {
  it('rejects sending to a conversation owned by a different tenant', async () => {
    const ownerTenantId = u();
    const foreignTenantId = u();
    const conversations = new FakeConversationRepository();
    const { conversationId, initiatorId } = seedDirectConversation(conversations, ownerTenantId);
    const deps = {
      conversations,
      idempotency: new FakeIdempotencyStore(),
      publisher: new FakeIntegrationEventPublisher(),
      settings: new FakeTenantSettingsProvider(),
      attachmentTracking: new FakeAttachmentTrackingRepository(),
      attachmentReservations: { reserve: async () => {}, consume: async () => false, purgeOlderThan: async () => 0 },
    };

    const own = await sendMessage(
      {
        tenantId: ownerTenantId,
        correlationId: u(),
        clientKey: u(),
        conversationId,
        senderUserId: initiatorId,
        body: 'hola',
      },
      deps,
    );
    expect(own.isSuccess).toBe(true);

    const foreign = await sendMessage(
      {
        tenantId: foreignTenantId,
        correlationId: u(),
        clientKey: u(),
        conversationId,
        senderUserId: initiatorId,
        body: 'hola desde otro tenant',
      },
      deps,
    );
    expectRejectedCrossTenant(foreign, ['Chat.Conversation.NotFound']);
  });
});

describe('Tenant isolation — markMessagesRead', () => {
  it('rejects marking read a conversation owned by a different tenant', async () => {
    const ownerTenantId = u();
    const foreignTenantId = u();
    const conversations = new FakeConversationRepository();
    const { conversationId, initiatorId } = seedDirectConversation(conversations, ownerTenantId);
    const deps = { conversations, messages: new FakeMessageRepository() };

    const foreign = await markMessagesRead(
      {
        tenantId: foreignTenantId,
        conversationId,
        userUserId: initiatorId,
        lastReadMessageId: u(),
      },
      deps,
    );
    expectRejectedCrossTenant(foreign, ['Chat.Conversation.NotFound']);
  });
});
