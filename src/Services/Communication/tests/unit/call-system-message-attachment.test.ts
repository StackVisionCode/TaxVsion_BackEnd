import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { appendCallSystemMessage } from '../../src/application/use-cases/append-call-system-message.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type {
  AttachmentTrackingRepository,
  AttachmentTrackingSnapshot,
  AttachmentTrackingStatus,
} from '../../src/application/ports/attachment-tracking-repository.js';
import type { CloudStorageMetadataClient } from '../../src/application/ports/cloudstorage-metadata-client.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';

const u = () => randomUUID();

class InMemoryTracking implements AttachmentTrackingRepository {
  readonly rows = new Map<string, AttachmentTrackingSnapshot>();
  async register(input: { fileId: string; messageId: string; conversationId: string; tenantId: string }): Promise<void> {
    this.rows.set(input.fileId, { ...input, status: 'Pending', updatedAtUtc: new Date() });
  }
  async markStatus(input: { fileId: string; status: AttachmentTrackingStatus }): Promise<AttachmentTrackingSnapshot | null> {
    const row = this.rows.get(input.fileId);
    if (!row) return null;
    const next = { ...row, status: input.status };
    this.rows.set(input.fileId, next);
    return next;
  }
  async findByFileId(fileId: string): Promise<AttachmentTrackingSnapshot | null> {
    return this.rows.get(fileId) ?? null;
  }
}

function setup(metadataStatus: string | null) {
  const tenantId = u();
  const started = Conversation.startDirect({
    tenantId,
    initiator: { userId: u(), displayName: 'Ana', actorType: 'TenantEmployee' },
    recipient: { userId: u(), displayName: 'Bob', actorType: 'CustomerPortal' },
  });
  if (!started.isSuccess) throw new Error('conversation');
  const conversation = started.value;
  const conversations = {
    findById: async () => conversation,
    save: async () => undefined,
  } as unknown as ConversationRepository;
  const tracking = new InMemoryTracking();
  const metadata = { getMetadata: async () => (metadataStatus ? { status: metadataStatus } : null) } as unknown as CloudStorageMetadataClient;
  const emitter = { emitToConversation: () => undefined } as unknown as RealtimeEmitter;
  return { tenantId, conversation, tracking, deps: { conversations, emitter, attachmentTracking: tracking, cloudStorageMetadata: metadata } };
}

describe('appendCallSystemMessage — transcript adjunto', () => {
  it('registra el transcript como adjunto del chat y lo deja disponible si CloudStorage ya lo escaneó', async () => {
    const { tenantId, conversation, tracking, deps } = setup('Available');
    const fileId = u();

    await appendCallSystemMessage({ tenantId, conversationId: conversation.id, body: 'Call transcript is ready', attachmentFileId: fileId }, deps);

    const row = await tracking.findByFileId(fileId);
    expect(row?.conversationId).toBe(conversation.id);
    expect(row?.status).toBe('Available');
  });

  it('queda Pending mientras CloudStorage no lo marque disponible (el consumer lo actualiza después)', async () => {
    const { tenantId, conversation, tracking, deps } = setup(null);
    const fileId = u();

    await appendCallSystemMessage({ tenantId, conversationId: conversation.id, body: 'Call transcript is ready', attachmentFileId: fileId }, deps);

    expect((await tracking.findByFileId(fileId))?.status).toBe('Pending');
  });

  it('sin adjunto no registra nada', async () => {
    const { tenantId, conversation, tracking, deps } = setup('Available');

    await appendCallSystemMessage({ tenantId, conversationId: conversation.id, body: 'Call · 2:34' }, deps);

    expect(tracking.rows.size).toBe(0);
  });
});
