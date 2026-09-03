import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import type { Conversation } from '../../src/domain/conversations/conversation.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type {
  AttachmentTrackingRepository,
  AttachmentTrackingSnapshot,
  AttachmentTrackingStatus,
} from '../../src/application/ports/attachment-tracking-repository.js';
import type { CloudStorageDownloadClient } from '../../src/application/ports/cloudstorage-download-client.js';
import type { CloudStorageMetadataClient } from '../../src/application/ports/cloudstorage-metadata-client.js';
import { getAttachmentDownloadUrl } from '../../src/application/use-cases/get-attachment-download-url.js';
import { getAttachmentMetadata } from '../../src/application/use-cases/get-attachment-metadata.js';

function u(): string {
  return randomUUID();
}

function trackingRepo(snapshot: AttachmentTrackingSnapshot | null): AttachmentTrackingRepository {
  return {
    async register() {},
    async markStatus() {
      return null;
    },
    async findByFileId() {
      return snapshot;
    },
  };
}

// Solo se usa `findById` + `isParticipant`; el resto del port no interviene.
function conversationRepo(members: string[] | null): ConversationRepository {
  const conversation =
    members === null ? null : ({ isParticipant: (uid: string) => members.includes(uid) } as unknown as Conversation);
  return { async findById() {
      return conversation;
    } } as unknown as ConversationRepository;
}

const downloadOk: CloudStorageDownloadClient = {
  async getDownloadUrl() {
    return { downloadUrl: 'https://minio.local/presigned', expiresAtUtc: '2026-01-01T00:00:00.000Z' };
  },
};

const downloadGone: CloudStorageDownloadClient = {
  async getDownloadUrl() {
    return null;
  },
};

const metadataOk: CloudStorageMetadataClient = {
  async getMetadata() {
    return { fileId: u(), sizeBytes: 2048, mimeType: 'application/pdf', originalName: 'w-2.pdf', status: 'Available' };
  },
};

/** CloudStorage aún escaneando (PendingScan): no debe promover el tracking. */
const metadataScanning: CloudStorageMetadataClient = {
  async getMetadata() {
    return { fileId: u(), sizeBytes: 2048, mimeType: 'application/pdf', originalName: 'w-2.pdf', status: 'PendingScan' };
  },
};

const tenantId = u();
const conversationId = u();
const fileId = u();
const memberId = u();

function snap(over: Partial<AttachmentTrackingSnapshot> = {}): AttachmentTrackingSnapshot {
  return {
    fileId,
    messageId: u(),
    conversationId,
    tenantId,
    status: 'Available',
    updatedAtUtc: new Date(),
    ...over,
  };
}

function cmd(over: { userId?: string; conversationId?: string; fileId?: string; tenantId?: string } = {}) {
  return {
    tenantId: over.tenantId ?? tenantId,
    userId: over.userId ?? memberId,
    conversationId: over.conversationId ?? conversationId,
    fileId: over.fileId ?? fileId,
  };
}

describe('conversation attachment access', () => {
  it('returns a presigned url for a participant when the attachment is Available', async () => {
    const result = await getAttachmentDownloadUrl(cmd(), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap()),
      cloudStorageDownload: downloadOk,
    });
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value.downloadUrl).toBe('https://minio.local/presigned');
      expect(result.value.expiresAtUtc).toBe('2026-01-01T00:00:00.000Z');
    }
  });

  it('rejects a non-participant', async () => {
    const result = await getAttachmentDownloadUrl(cmd({ userId: u() }), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap()),
      cloudStorageDownload: downloadOk,
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Conversation.NotParticipant');
  });

  it('hides an attachment referenced from a different conversation (anti-enumeration)', async () => {
    const result = await getAttachmentDownloadUrl(cmd({ conversationId: u() }), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap()), // tracking.conversationId != command.conversationId
      cloudStorageDownload: downloadOk,
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Attachment.NotFound');
  });

  it('hides an attachment from another tenant', async () => {
    const result = await getAttachmentDownloadUrl(cmd(), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap({ tenantId: u() })),
      cloudStorageDownload: downloadOk,
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Attachment.NotFound');
  });

  it('returns NotFound when there is no tracking for the file', async () => {
    const result = await getAttachmentDownloadUrl(cmd(), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(null),
      cloudStorageDownload: downloadOk,
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Attachment.NotFound');
  });

  it('refuses download while the attachment is still Pending scan', async () => {
    const result = await getAttachmentDownloadUrl(cmd(), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap({ status: 'Pending' })),
      cloudStorageDownload: downloadOk,
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Attachment.Pending');
  });

  it.each<AttachmentTrackingStatus>(['Infected', 'BlockedByPolicy'])(
    'refuses download for a %s attachment without leaking scan internals',
    async (status) => {
      const result = await getAttachmentDownloadUrl(cmd(), {
        conversations: conversationRepo([memberId]),
        attachmentTracking: trackingRepo(snap({ status })),
        cloudStorageDownload: downloadOk,
      });
      expect(result.isSuccess).toBe(false);
      if (!result.isSuccess) {
        expect(result.error.code).toBe('Chat.Attachment.Unavailable');
        expect(result.error.message).not.toMatch(/virus|scan|quarantine|infected/i);
      }
    },
  );

  it('reports Deleted for a Deleted attachment', async () => {
    const result = await getAttachmentDownloadUrl(cmd(), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap({ status: 'Deleted' })),
      cloudStorageDownload: downloadOk,
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Attachment.Deleted');
  });

  it('maps a CloudStorage 404 on an Available file to Deleted (delete race)', async () => {
    const result = await getAttachmentDownloadUrl(cmd(), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap()),
      cloudStorageDownload: downloadGone,
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Attachment.Deleted');
  });

  it('returns metadata + status for a participant', async () => {
    const result = await getAttachmentMetadata(cmd(), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap()),
      cloudStorageMetadata: metadataOk,
    });
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value.fileName).toBe('w-2.pdf');
      expect(result.value.sizeBytes).toBe(2048);
      expect(result.value.status).toBe('Available');
    }
  });

  it('does not resolve metadata for a flagged attachment but still returns its status', async () => {
    const result = await getAttachmentMetadata(cmd(), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap({ status: 'Infected' })),
      cloudStorageMetadata: metadataOk,
    });
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value.status).toBe('Infected');
      expect(result.value.fileName).toBeNull();
      expect(result.value.sizeBytes).toBeNull();
    }
  });

  it('self-heals a Pending tracking to Available when CloudStorage reports Available (evento perdido)', async () => {
    let healedTo: AttachmentTrackingStatus | null = null;
    const healingRepo: AttachmentTrackingRepository = {
      async register() {},
      async markStatus(input) {
        healedTo = input.status;
        return null;
      },
      async findByFileId() {
        return snap({ status: 'Pending' });
      },
    };
    const result = await getAttachmentMetadata(cmd(), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: healingRepo,
      cloudStorageMetadata: metadataOk,
    });
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value.status).toBe('Available');
    }
    expect(healedTo).toBe('Available'); // sanó el tracking, no solo devolvió el estado
  });

  it('mantiene Pending si CloudStorage sigue escaneando (no promueve de más)', async () => {
    const result = await getAttachmentMetadata(cmd(), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap({ status: 'Pending' })),
      cloudStorageMetadata: metadataScanning,
    });
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value.status).toBe('Pending');
    }
  });

  it('rejects metadata for a non-participant', async () => {
    const result = await getAttachmentMetadata(cmd({ userId: u() }), {
      conversations: conversationRepo([memberId]),
      attachmentTracking: trackingRepo(snap()),
      cloudStorageMetadata: metadataOk,
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Conversation.NotParticipant');
  });
});
