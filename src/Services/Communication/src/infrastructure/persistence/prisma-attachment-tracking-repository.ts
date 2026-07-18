import type { PrismaClient } from '@prisma/client';
import type {
  AttachmentTrackingRepository,
  AttachmentTrackingSnapshot,
  AttachmentTrackingStatus,
} from '../../application/ports/attachment-tracking-repository.js';

function toSnapshot(row: {
  FileId: string;
  MessageId: string;
  ConversationId: string;
  TenantId: string;
  Status: string;
  UpdatedAtUtc: Date;
}): AttachmentTrackingSnapshot {
  return {
    fileId: row.FileId,
    messageId: row.MessageId,
    conversationId: row.ConversationId,
    tenantId: row.TenantId,
    status: row.Status as AttachmentTrackingStatus,
    updatedAtUtc: row.UpdatedAtUtc,
  };
}

export class PrismaAttachmentTrackingRepository implements AttachmentTrackingRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async register(input: {
    fileId: string;
    messageId: string;
    conversationId: string;
    tenantId: string;
  }): Promise<void> {
    await this.prisma.attachmentTracking.upsert({
      where: { FileId: input.fileId },
      create: {
        FileId: input.fileId,
        MessageId: input.messageId,
        ConversationId: input.conversationId,
        TenantId: input.tenantId,
        Status: 'Pending',
      },
      update: {
        MessageId: input.messageId,
        ConversationId: input.conversationId,
        TenantId: input.tenantId,
      },
    });
  }

  async markStatus(input: {
    fileId: string;
    status: AttachmentTrackingStatus;
  }): Promise<AttachmentTrackingSnapshot | null> {
    const row = await this.prisma.attachmentTracking
      .update({ where: { FileId: input.fileId }, data: { Status: input.status } })
      .catch(() => null);
    return row ? toSnapshot(row) : null;
  }

  async findByFileId(fileId: string): Promise<AttachmentTrackingSnapshot | null> {
    const row = await this.prisma.attachmentTracking.findUnique({ where: { FileId: fileId } });
    return row ? toSnapshot(row) : null;
  }
}
