import type { PrismaClient } from '@prisma/client';
import type { AttachmentUploadReservationRepository } from '../../application/ports/attachment-upload-reservation-repository.js';

export class PrismaAttachmentUploadReservationRepository implements AttachmentUploadReservationRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async reserve(input: { fileId: string; tenantId: string; conversationId: string; uploadedByUserId: string }): Promise<void> {
    await this.prisma.attachmentUploadReservation.create({
      data: {
        FileId: input.fileId,
        TenantId: input.tenantId,
        ConversationId: input.conversationId,
        UploadedByUserId: input.uploadedByUserId,
      },
    });
  }

  async consume(input: { fileId: string; tenantId: string; conversationId: string; uploadedByUserId: string }): Promise<boolean> {
    // Un solo DELETE condicional: dos envíos concurrentes del mismo fileId no pueden ganar ambos.
    const result = await this.prisma.attachmentUploadReservation.deleteMany({
      where: {
        FileId: input.fileId,
        TenantId: input.tenantId,
        ConversationId: input.conversationId,
        UploadedByUserId: input.uploadedByUserId,
      },
    });
    return result.count === 1;
  }

  async purgeOlderThan(cutoff: Date): Promise<number> {
    const result = await this.prisma.attachmentUploadReservation.deleteMany({ where: { CreatedAtUtc: { lt: cutoff } } });
    return result.count;
  }
}
