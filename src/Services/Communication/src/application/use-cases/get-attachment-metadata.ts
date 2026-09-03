import { Result } from '../../domain/shared/result.js';
import {
  authorizeConversationAttachment,
  type AuthorizeConversationAttachmentCommand,
  type AuthorizeConversationAttachmentDeps,
} from './authorize-conversation-attachment.js';
import type { CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import type { AttachmentTrackingStatus } from '../ports/attachment-tracking-repository.js';

/**
 * Metadata de un adjunto de chat para el que llama, previa autorizacion de
 * membresia. Devuelve siempre el `status` de escaneo (de AttachmentTracking, sin
 * round-trip) para que el front decida la UI; nombre/tamano se resuelven best-effort
 * contra CloudStorage y quedan `null` si el archivo ya no esta (borrado/bloqueado).
 */
export type GetAttachmentMetadataCommand = AuthorizeConversationAttachmentCommand;

export interface AttachmentMetadataResult {
  readonly fileId: string;
  readonly fileName: string | null;
  readonly sizeBytes: number | null;
  readonly contentType: string | null;
  readonly status: AttachmentTrackingStatus;
}

export interface GetAttachmentMetadataDeps extends AuthorizeConversationAttachmentDeps {
  readonly cloudStorageMetadata: CloudStorageMetadataClient;
}

export async function getAttachmentMetadata(
  command: GetAttachmentMetadataCommand,
  deps: GetAttachmentMetadataDeps,
): Promise<Result<AttachmentMetadataResult>> {
  const authorized = await authorizeConversationAttachment(command, deps);
  if (!authorized.isSuccess) return Result.fail(authorized.error);
  const tracking = authorized.value;

  // Solo pedimos metadata cuando el archivo puede existir en Main (Pending/Available).
  // Para infectado/bloqueado/borrado devolvemos solo el estado — el front muestra
  // "unavailable" sin exponer detalles del escaneo.
  const canResolve = tracking.status === 'Available' || tracking.status === 'Pending';
  const meta = canResolve
    ? await deps.cloudStorageMetadata.getMetadata(command.tenantId, command.fileId).catch(() => null)
    : null;

  return Result.ok({
    fileId: command.fileId,
    fileName: meta?.originalName ?? null,
    sizeBytes: meta?.sizeBytes ?? null,
    contentType: meta?.mimeType ?? null,
    status: tracking.status,
  });
}
