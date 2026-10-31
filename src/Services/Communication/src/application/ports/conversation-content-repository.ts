import type { AttachmentCategory } from '../../domain/attachments/attachment-category.js';
import type { AttachmentTrackingStatus } from './attachment-tracking-repository.js';

/**
 * F8d (ADD-01 §13, T-16): contenido compartido de una conversación para las pestañas de Info, paginado por fecha
 * (más reciente primero). Solo mensajes vivos; los adjuntos rechazados por el escaneo no aparecen.
 */
export interface GalleryAttachment {
  readonly fileId: string;
  readonly messageId: string;
  readonly senderId: string;
  readonly category: AttachmentCategory;
  readonly contentType: string | null;
  readonly fileName: string | null;
  readonly sizeBytes: number | null;
  readonly status: AttachmentTrackingStatus;
  readonly createdAtUtc: Date;
}

export interface GalleryLink {
  readonly id: string;
  readonly messageId: string;
  readonly senderId: string;
  readonly url: string;
  readonly domain: string;
  readonly createdAtUtc: Date;
}

interface PageWindow {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly take: number;
  /** Cursor compuesto (fecha, id): varios enlaces de un mensaje comparten fecha. */
  readonly before?: { readonly at: Date; readonly id: string } | null;
  /** D-21: el cliente de un grupo no ve lo anterior a su ingreso. */
  readonly notBeforeUtc?: Date | null;
}

export interface ConversationContentRepository {
  listAttachments(input: PageWindow & { categories: readonly AttachmentCategory[] }): Promise<GalleryAttachment[]>;
  listLinks(input: PageWindow): Promise<GalleryLink[]>;
}
