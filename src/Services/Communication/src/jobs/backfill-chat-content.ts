import type { Message, PrismaClient } from '@prisma/client';
import { connectPrisma, prisma as sharedPrisma } from '../infrastructure/persistence/prisma-client.js';
import { logger } from '../infrastructure/logger/logger.js';
import { HttpCloudStorageMetadataClient } from '../infrastructure/cloudstorage/http-cloudstorage-metadata-client.js';
import { ServiceTokenClient } from '../infrastructure/auth/service-token-client.js';
import { categorizeContentType } from '../domain/attachments/attachment-category.js';
import { messageLinkRows } from '../infrastructure/persistence/message-link-rows.js';
import { toDomainMessage } from '../infrastructure/persistence/conversation-mapper.js';

/**
 * F8d (T-17): relleno idempotente de lo anterior a F8d, como job único (no en la migración de esquema):
 *   1. Adjuntos sin categoría → pide su metadata a CloudStorage y guarda tipo real, categoría, nombre y tamaño.
 *      Si CloudStorage no responde, el adjunto queda para la siguiente corrida.
 *   2. Mensajes de texto con enlaces y sin filas en `MessageLink` → las crea.
 * Uso: `npm run job:backfill-chat-content` (se puede repetir; procesa por lotes).
 */
const BATCH = 200;

async function backfillAttachments(prisma: PrismaClient, metadata: HttpCloudStorageMetadataClient): Promise<number> {
  let done = 0;
  let cursor: string | null = null;
  for (;;) {
    const rows: { FileId: string; TenantId: string }[] = await prisma.attachmentTracking.findMany({
      where: { Category: null, ...(cursor ? { FileId: { gt: cursor } } : {}) },
      select: { FileId: true, TenantId: true },
      orderBy: { FileId: 'asc' },
      take: BATCH,
    });
    if (rows.length === 0) return done;
    for (const row of rows) {
      const meta = await metadata.getMetadata(row.TenantId, row.FileId).catch(() => null);
      if (!meta) continue;
      await prisma.attachmentTracking.update({
        where: { FileId: row.FileId },
        data: {
          Category: categorizeContentType(meta.mimeType),
          ContentType: meta.mimeType?.slice(0, 128) ?? null,
          FileName: meta.originalName?.slice(0, 260) ?? null,
          SizeBytes: BigInt(meta.sizeBytes),
        },
      });
      done++;
    }
    cursor = rows.at(-1)?.FileId ?? null;
  }
}

async function backfillLinks(prisma: PrismaClient): Promise<number> {
  let done = 0;
  let cursor: string | null = null;
  for (;;) {
    const rows: Message[] = await prisma.message.findMany({
      where: {
        Kind: 'Text',
        IsDeleted: false,
        OR: [{ Body: { contains: 'http' } }, { Body: { contains: 'www.' } }],
        Links: { none: {} },
        ...(cursor ? { Id: { gt: cursor } } : {}),
      },
      orderBy: { Id: 'asc' },
      take: BATCH,
    });
    if (rows.length === 0) return done;
    const links = rows.flatMap((row) => messageLinkRows(toDomainMessage(row)));
    if (links.length > 0) await prisma.messageLink.createMany({ data: links });
    done += links.length;
    cursor = rows.at(-1)?.Id ?? null;
  }
}

async function main(): Promise<void> {
  await connectPrisma();
  try {
    const links = await backfillLinks(sharedPrisma);
    logger.info({ links }, 'backfill-chat-content: enlaces creados');
    const metadata = new HttpCloudStorageMetadataClient(new ServiceTokenClient());
    const attachments = await backfillAttachments(sharedPrisma, metadata);
    logger.info({ attachments }, 'backfill-chat-content: adjuntos categorizados');
  } finally {
    await sharedPrisma.$disconnect();
  }
}

void main().catch((err: unknown) => {
  logger.error({ err }, 'backfill-chat-content falló');
  process.exitCode = 1;
});
