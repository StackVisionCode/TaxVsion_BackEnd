import { Prisma, type PrismaClient } from '@prisma/client';
import type {
  ConversationContentRepository,
  GalleryAttachment,
  GalleryLink,
} from '../../application/ports/conversation-content-repository.js';
import type { AttachmentCategory } from '../../domain/attachments/attachment-category.js';
import type { AttachmentTrackingStatus } from '../../application/ports/attachment-tracking-repository.js';

/**
 * F8d: pestañas de Info. Consultas crudas con JOIN al mensaje (vivo) para no traer de más; los opcionales van como
 * fragmentos, nunca como parámetro NULL (Prisma los tipa int en SQL Server).
 */
export class PrismaConversationContentRepository implements ConversationContentRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async listAttachments(input: {
    tenantId: string;
    conversationId: string;
    take: number;
    before?: { at: Date; id: string } | null;
    notBeforeUtc?: Date | null;
    categories: readonly AttachmentCategory[];
  }): Promise<GalleryAttachment[]> {
    if (input.categories.length === 0) return [];
    const conditions = [
      Prisma.sql`a.[TenantId] = ${input.tenantId}`,
      Prisma.sql`a.[ConversationId] = ${input.conversationId}`,
      Prisma.sql`a.[Category] IN (${Prisma.join([...input.categories])})`,
      // T-20: lo que está en revisión se lista como "Under review"; lo rechazado no.
      Prisma.sql`a.[Status] IN ('Pending', 'Available', 'PendingReview')`,
      Prisma.sql`m.[IsDeleted] = 0`,
      ...timeWindow('a', 'FileId', input),
    ];
    const rows = await this.prisma.$queryRaw<
      {
        FileId: string;
        MessageId: string;
        SenderId: string;
        Category: string;
        ContentType: string | null;
        FileName: string | null;
        SizeBytes: bigint | number | null;
        Status: string;
        CreatedAtUtc: Date;
      }[]
    >`
      SELECT TOP (${input.take}) a.[FileId], a.[MessageId], m.[SenderId], a.[Category], a.[ContentType], a.[FileName],
             a.[SizeBytes], a.[Status], a.[CreatedAtUtc]
      FROM [dbo].[AttachmentTracking] a
      JOIN [dbo].[Message] m ON m.[Id] = a.[MessageId] AND m.[TenantId] = a.[TenantId]
      WHERE ${Prisma.join(conditions, ' AND ')}
      ORDER BY a.[CreatedAtUtc] DESC, a.[FileId] DESC`;
    return rows.map((r) => ({
      fileId: r.FileId,
      messageId: r.MessageId,
      senderId: r.SenderId,
      category: r.Category as AttachmentCategory,
      contentType: r.ContentType,
      fileName: r.FileName,
      sizeBytes: r.SizeBytes === null ? null : Number(r.SizeBytes),
      status: r.Status as AttachmentTrackingStatus,
      createdAtUtc: r.CreatedAtUtc,
    }));
  }

  async listLinks(input: {
    tenantId: string;
    conversationId: string;
    take: number;
    before?: { at: Date; id: string } | null;
    notBeforeUtc?: Date | null;
  }): Promise<GalleryLink[]> {
    const conditions = [
      Prisma.sql`l.[TenantId] = ${input.tenantId}`,
      Prisma.sql`l.[ConversationId] = ${input.conversationId}`,
      Prisma.sql`m.[IsDeleted] = 0`,
      ...timeWindow('l', 'Id', input),
    ];
    const rows = await this.prisma.$queryRaw<
      { Id: string; MessageId: string; SenderId: string; Url: string; Domain: string; CreatedAtUtc: Date }[]
    >`
      SELECT TOP (${input.take}) l.[Id], l.[MessageId], m.[SenderId], l.[Url], l.[Domain], l.[CreatedAtUtc]
      FROM [dbo].[MessageLink] l
      JOIN [dbo].[Message] m ON m.[Id] = l.[MessageId] AND m.[TenantId] = l.[TenantId]
      WHERE ${Prisma.join(conditions, ' AND ')}
      ORDER BY l.[CreatedAtUtc] DESC, l.[Id] DESC`;
    return rows.map((r) => ({
      id: r.Id,
      messageId: r.MessageId,
      senderId: r.SenderId,
      url: r.Url,
      domain: r.Domain,
      createdAtUtc: r.CreatedAtUtc,
    }));
  }
}

function timeWindow(
  alias: 'a' | 'l',
  idColumn: 'FileId' | 'Id',
  input: { before?: { at: Date; id: string } | null; notBeforeUtc?: Date | null },
): Prisma.Sql[] {
  const column = Prisma.raw(`${alias}.[CreatedAtUtc]`);
  const id = Prisma.raw(`${alias}.[${idColumn}]`);
  const before = input.before;
  return [
    ...(before ? [Prisma.sql`(${column} < ${before.at} OR (${column} = ${before.at} AND ${id} < ${before.id}))`] : []),
    ...(input.notBeforeUtc ? [Prisma.sql`${column} >= ${input.notBeforeUtc}`] : []),
  ];
}
