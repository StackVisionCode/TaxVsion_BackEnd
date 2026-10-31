import { categorizeContentType } from '../../domain/attachments/attachment-category.js';
import type { AttachmentTrackingRepository } from '../ports/attachment-tracking-repository.js';
import type { CloudStorageFileMetadata, CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';

/**
 * F8d: guarda en el tracking el tipo real del adjunto y su categoría. Best-effort: si CloudStorage no responde,
 * el job de relleno (T-17) lo completa después. Devuelve la metadata leída para reutilizarla.
 */
export async function recordAttachmentMetadata(
  input: { tenantId: string; fileId: string; known?: CloudStorageFileMetadata | null },
  deps: { attachmentTracking: AttachmentTrackingRepository; cloudStorageMetadata?: CloudStorageMetadataClient | undefined },
): Promise<CloudStorageFileMetadata | null> {
  if (!deps.attachmentTracking.recordMetadata) return input.known ?? null;
  const meta =
    input.known ?? (deps.cloudStorageMetadata ? await deps.cloudStorageMetadata.getMetadata(input.tenantId, input.fileId).catch(() => null) : null);
  if (!meta) return null;
  await deps.attachmentTracking.recordMetadata({
    fileId: input.fileId,
    contentType: meta.mimeType,
    fileName: meta.originalName,
    sizeBytes: meta.sizeBytes,
    category: categorizeContentType(meta.mimeType),
  });
  return meta;
}
