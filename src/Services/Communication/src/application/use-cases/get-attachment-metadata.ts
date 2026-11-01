import { Result } from '../../domain/shared/result.js';
import {
  authorizeConversationAttachment,
  type AuthorizeConversationAttachmentCommand,
  type AuthorizeConversationAttachmentDeps,
} from './authorize-conversation-attachment.js';
import type { CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import { recordAttachmentMetadata } from './record-attachment-metadata.js';
import type { AttachmentTrackingStatus } from '../ports/attachment-tracking-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { syncAttachmentStatusFromStorage } from './sync-attachment-status.js';

/**
 * Metadata de un adjunto de chat para el que llama, previa autorizacion de
 * membresia. Devuelve siempre el `status` de escaneo (de AttachmentTracking, sin
 * round-trip) para que el front decida la UI; nombre/tamano se resuelven best-effort
 * contra CloudStorage y quedan `null` si el archivo esta infectado o ya no esta.
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
  readonly emitter?: RealtimeEmitter | undefined;
}

const RESOLVABLE: ReadonlySet<AttachmentTrackingStatus> = new Set(['Available', 'Pending', 'PendingReview', 'BlockedByPolicy']);

export async function getAttachmentMetadata(
  command: GetAttachmentMetadataCommand,
  deps: GetAttachmentMetadataDeps,
): Promise<Result<AttachmentMetadataResult>> {
  const authorized = await authorizeConversationAttachment(command, deps);
  if (!authorized.isSuccess) return Result.fail(authorized.error);
  const tracking = authorized.value;

  // Infectado o borrado: solo el estado ("unavailable", sin detalles del escaneo). En revisión o bloqueado por la
  // política el archivo sigue existiendo y el nombre ya lo vieron al enviarlo: se muestra (E2E F8: salía "Attachment").
  const canResolve = RESOLVABLE.has(tracking.status);
  const meta = canResolve
    ? await deps.cloudStorageMetadata.getMetadata(command.tenantId, command.fileId).catch(() => null)
    : null;

  // Self-heal: si el tracking sigue 'Pending' pero CloudStorage ya marcó el archivo 'Available',
  // el evento `cloudstorage.file.available.v1` no llegó/consumió — no dejamos el adjunto en "Scanning"
  // para siempre. CloudStorage es la fuente autoritativa del escaneo: promovemos y sanamos el tracking
  // (best-effort) para que el próximo emit y las demás vistas ya lo vean disponible.
  // F8d: aprovecha la lectura para dejar el tipo real y la categoría en el tracking (Info por pestañas).
  if (meta) await recordAttachmentMetadata({ tenantId: command.tenantId, fileId: command.fileId, known: meta }, deps).catch(() => undefined);

  // T-20: también sana "en revisión", para que el chat diga "Under review" y no "Scanning".
  const status =
    tracking.status === 'Pending' && meta
      ? ((await syncAttachmentStatusFromStorage({ tenantId: command.tenantId, fileId: command.fileId, known: meta }, deps)) ?? tracking.status)
      : tracking.status;

  return Result.ok({
    fileId: command.fileId,
    fileName: meta?.originalName ?? null,
    sizeBytes: meta?.sizeBytes ?? null,
    contentType: meta?.mimeType ?? null,
    status,
  });
}
