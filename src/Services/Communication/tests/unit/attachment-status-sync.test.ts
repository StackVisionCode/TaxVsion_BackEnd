import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import type { Conversation } from '../../src/domain/conversations/conversation.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type {
  AttachmentTrackingRepository,
  AttachmentTrackingSnapshot,
  AttachmentTrackingStatus,
} from '../../src/application/ports/attachment-tracking-repository.js';
import type { CloudStorageMetadataClient } from '../../src/application/ports/cloudstorage-metadata-client.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { syncAttachmentStatusFromStorage } from '../../src/application/use-cases/sync-attachment-status.js';
import { getAttachmentDownloadUrl } from '../../src/application/use-cases/get-attachment-download-url.js';

const tenantId = randomUUID();
const conversationId = randomUUID();
const fileId = randomUUID();
const userId = randomUUID();

/** Tracking en memoria que sí guarda el cambio de estado. */
function tracking(status: AttachmentTrackingStatus | null) {
  let row: AttachmentTrackingSnapshot | null = status
    ? { fileId, messageId: randomUUID(), conversationId, tenantId, status, updatedAtUtc: new Date() }
    : null;
  const repo: AttachmentTrackingRepository = {
    async register() {},
    async markStatus(input) {
      if (!row) return null;
      row = { ...row, status: input.status, updatedAtUtc: new Date() };
      return row;
    },
    async findByFileId() {
      return row;
    },
  };
  return { repo, current: () => row?.status ?? null };
}

function storage(status: string | null): CloudStorageMetadataClient {
  return {
    async getMetadata() {
      return { fileId, sizeBytes: 1024, mimeType: 'video/webm', originalName: 'voice-note.webm', status };
    },
  };
}

function emitter() {
  const events: Array<{ event: string; payload: unknown }> = [];
  const value = {
    emitToConversation: (input: { event: string; envelope: { payload: unknown } }) => events.push({ event: input.event, payload: input.envelope.payload }),
  } as unknown as RealtimeEmitter;
  return { value, events };
}

describe('Estado del adjunto tomado de CloudStorage (evento available adelantado o perdido)', () => {
  it('un Pending que CloudStorage ya liberó pasa a Available y avisa a la conversación', async () => {
    const t = tracking('Pending');
    const e = emitter();

    const status = await syncAttachmentStatusFromStorage({ tenantId, fileId }, { attachmentTracking: t.repo, cloudStorageMetadata: storage('Available'), emitter: e.value });

    expect(status).toBe('Available');
    expect(t.current()).toBe('Available');
    expect(e.events).toEqual([expect.objectContaining({ event: 'chat.message.attachment_flagged', payload: expect.objectContaining({ fileId, status: 'Available' }) })]);
  });

  it('si CloudStorage sigue escaneando, queda Pending y no avisa', async () => {
    const t = tracking('Pending');
    const e = emitter();

    const status = await syncAttachmentStatusFromStorage({ tenantId, fileId }, { attachmentTracking: t.repo, cloudStorageMetadata: storage('Scanning'), emitter: e.value });

    expect(status).toBe('Pending');
    expect(e.events).toHaveLength(0);
  });

  it('copia también los rechazos y la revisión; un estado ya final no se toca', async () => {
    for (const final of ['PendingReview', 'Infected', 'BlockedByPolicy'] as const) {
      const t = tracking('Pending');
      expect(await syncAttachmentStatusFromStorage({ tenantId, fileId }, { attachmentTracking: t.repo, cloudStorageMetadata: storage(final) })).toBe(final);
    }
    const done = tracking('Deleted');
    expect(await syncAttachmentStatusFromStorage({ tenantId, fileId }, { attachmentTracking: done.repo, cloudStorageMetadata: storage('Available') })).toBe('Deleted');
    expect(await syncAttachmentStatusFromStorage({ tenantId, fileId }, { attachmentTracking: tracking(null).repo, cloudStorageMetadata: storage('Available') })).toBeNull();
  });

  it('el play (download-url) de una nota recién escaneada ya no responde Pending', async () => {
    const t = tracking('Pending');
    const conversations = {
      async findById() {
        return { isParticipant: (id: string) => id === userId, historyVisibleFrom: () => null } as unknown as Conversation;
      },
    } as unknown as ConversationRepository;
    const deps = {
      conversations,
      attachmentTracking: t.repo,
      cloudStorageDownload: { async getDownloadUrl() { return { downloadUrl: 'https://minio.local/voice', expiresAtUtc: '2026-11-01T05:00:00Z' }; } },
    };

    const before = await getAttachmentDownloadUrl({ tenantId, userId, conversationId, fileId }, { ...deps, cloudStorageMetadata: storage('Scanning') });
    expect(before.isSuccess).toBe(false);
    if (!before.isSuccess) expect(before.error.code).toBe('Chat.Attachment.Pending');

    const after = await getAttachmentDownloadUrl({ tenantId, userId, conversationId, fileId }, { ...deps, cloudStorageMetadata: storage('Available') });
    expect(after.isSuccess).toBe(true);
    if (after.isSuccess) expect(after.value.downloadUrl).toBe('https://minio.local/voice');
  });
});
