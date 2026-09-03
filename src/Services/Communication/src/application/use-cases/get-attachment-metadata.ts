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

  // Self-heal: si el tracking sigue 'Pending' pero CloudStorage ya marcó el archivo 'Available',
  // el evento `cloudstorage.file.available.v1` no llegó/consumió — no dejamos el adjunto en "Scanning"
  // para siempre. CloudStorage es la fuente autoritativa del escaneo: promovemos y sanamos el tracking
  // (best-effort) para que el próximo emit y las demás vistas ya lo vean disponible.
  let status = tracking.status;
  if (status === 'Pending' && meta?.status === 'Available') {
    status = 'Available';
    await deps.attachmentTracking.markStatus({ fileId: command.fileId, status: 'Available' }).catch(() => undefined);
  }

  return Result.ok({
    fileId: command.fileId,
    fileName: meta?.originalName ?? null,
    sizeBytes: meta?.sizeBytes ?? null,
    contentType: meta?.mimeType ?? null,
    status,
  });
}
