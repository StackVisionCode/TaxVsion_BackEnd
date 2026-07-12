/**
 * Tracking de estado de archivos adjuntos a mensajes de chat, alimentado por:
 *  - `send-message.ts` (register, al insertar el adjunto en el mensaje)
 *  - los consumers de `cloudstorage.file.available/infected/deleted` (markStatus)
 * Permite reflejar en el chat cuando un adjunto fue marcado como infectado o
 * eliminado por CloudStorage, sin depender de un round-trip HTTP.
 */
export type AttachmentTrackingStatus = 'Pending' | 'Available' | 'Infected' | 'Deleted' | 'BlockedByPolicy';

export interface AttachmentTrackingSnapshot {
  readonly fileId: string;
  readonly messageId: string;
  readonly conversationId: string;
  readonly tenantId: string;
  readonly status: AttachmentTrackingStatus;
  readonly updatedAtUtc: Date;
}

export interface AttachmentTrackingRepository {
  register(input: {
    fileId: string;
    messageId: string;
    conversationId: string;
    tenantId: string;
  }): Promise<void>;

  markStatus(input: { fileId: string; status: AttachmentTrackingStatus }): Promise<AttachmentTrackingSnapshot | null>;

  findByFileId(fileId: string): Promise<AttachmentTrackingSnapshot | null>;
}
