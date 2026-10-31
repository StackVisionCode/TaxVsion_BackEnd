/**
 * F8d (ADD-01): categoría de un adjunto por su tipo REAL (el detectado por CloudStorage; el declarado solo
 * mientras no hay detección). Así un .exe renombrado a .jpg no aparece en "Media".
 */
export const AttachmentCategory = {
  Image: 'Image',
  Video: 'Video',
  Audio: 'Audio',
  Document: 'Document',
  Other: 'Other',
} as const;
export type AttachmentCategory = (typeof AttachmentCategory)[keyof typeof AttachmentCategory];

const DOCUMENT_TYPES = new Set([
  'application/pdf',
  'text/plain',
  'text/csv',
  'application/rtf',
  'application/msword',
  'application/vnd.ms-excel',
  'application/vnd.ms-powerpoint',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/vnd.openxmlformats-officedocument.presentationml.presentation',
  'application/vnd.oasis.opendocument.text',
  'application/vnd.oasis.opendocument.spreadsheet',
]);

export function categorizeContentType(contentType: string | null | undefined): AttachmentCategory {
  const type = (contentType ?? '').split(';')[0]?.trim().toLowerCase() ?? '';
  if (type.startsWith('image/')) return AttachmentCategory.Image;
  if (type.startsWith('video/')) return AttachmentCategory.Video;
  if (type.startsWith('audio/')) return AttachmentCategory.Audio;
  if (DOCUMENT_TYPES.has(type)) return AttachmentCategory.Document;
  return AttachmentCategory.Other;
}

/** Pestañas de Info → categorías que agrupan. */
export const GALLERY_TABS = {
  Media: [AttachmentCategory.Image, AttachmentCategory.Video],
  Documents: [AttachmentCategory.Document, AttachmentCategory.Other],
  Audio: [AttachmentCategory.Audio],
} as const;
export type GalleryTab = keyof typeof GALLERY_TABS | 'Links';
