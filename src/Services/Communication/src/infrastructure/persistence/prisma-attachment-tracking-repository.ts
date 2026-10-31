import type { PrismaClient } from '@prisma/client';
import type { AttachmentCategory } from '../../domain/attachments/attachment-category.js';
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
    // F11 (E-259): solo se crea; un archivo ya ligado a otro mensaje no se reasigna (antes el upsert
    // movía TenantId/ConversationId). Reintentar el mismo mensaje es idempotente.
    const existing = await this.prisma.attachmentTracking.findUnique({ where: { FileId: input.fileId } });
    if (existing) {
      const sameBinding =
        existing.MessageId === input.messageId && existing.ConversationId === input.conversationId && existing.TenantId === input.tenantId;
      if (sameBinding) return;
      throw new AttachmentAlreadyBoundError(input.fileId);
    }
    await this.prisma.attachmentTracking.create({
      data: {
        FileId: input.fileId,
        MessageId: input.messageId,
        ConversationId: input.conversationId,
        TenantId: input.tenantId,
        Status: 'Pending',
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

  async recordMetadata(input: {
    fileId: string;
    contentType: string | null;
    fileName: string | null;
    sizeBytes: number | null;
    category: AttachmentCategory;
  }): Promise<void> {
    await this.prisma.attachmentTracking
      .update({
        where: { FileId: input.fileId },
        data: {
          Category: input.category,
          ContentType: input.contentType?.slice(0, 128) ?? null,
          FileName: input.fileName?.slice(0, 260) ?? null,
          SizeBytes: input.sizeBytes === null ? null : BigInt(input.sizeBytes),
        },
      })
      .catch(() => undefined);
  }

  async findByFileId(fileId: string): Promise<AttachmentTrackingSnapshot | null> {
    const row = await this.prisma.attachmentTracking.findUnique({ where: { FileId: fileId } });
    return row ? toSnapshot(row) : null;
  }
}

/** Un adjunto ya pertenece a otro mensaje: no se reasigna (F11, E-259). */
export class AttachmentAlreadyBoundError extends Error {
  constructor(fileId: string) {
    super(`Attachment ${fileId} is already bound to another message.`);
    this.name = 'AttachmentAlreadyBoundError';
  }
}
