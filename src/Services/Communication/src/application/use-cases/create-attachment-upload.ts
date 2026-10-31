import { Result } from '../../domain/shared/result.js';
import type { AttachmentUploadReservationRepository } from '../ports/attachment-upload-reservation-repository.js';
import {
  authorizeConversationParticipant,
  type AuthorizeConversationParticipantDeps,
} from './authorize-conversation-participant.js';
import type {
  CloudStorageInitiatedUpload,
  CloudStorageUploadClient,
} from '../ports/cloudstorage-upload-client.js';

/**
 * Media el INICIO de la subida de un adjunto de chat: verifica membresia y pide
 * a CloudStorage (como Service) una URL presignada para un blob
 * `ownerType=Communication`. El browser sube el binario directo a MinIO con esa
 * URL; luego finaliza con `complete-attachment-upload` y envia el mensaje.
 */
export interface CreateAttachmentUploadCommand {
  readonly tenantId: string;
  readonly userId: string;
  readonly conversationId: string;
  readonly originalName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
}

export interface CreateAttachmentUploadDeps extends AuthorizeConversationParticipantDeps {
  readonly cloudStorageUpload: CloudStorageUploadClient;
  /** F11 (E-259): deja constancia de quién sube qué y para qué conversación (lo exige send-message). */
  readonly attachmentReservations: AttachmentUploadReservationRepository;
}

export async function createAttachmentUpload(
  command: CreateAttachmentUploadCommand,
  deps: CreateAttachmentUploadDeps,
): Promise<Result<CloudStorageInitiatedUpload>> {
  const authorized = await authorizeConversationParticipant(command, deps);
  if (!authorized.isSuccess) return Result.fail(authorized.error);

  const initiated = await deps.cloudStorageUpload.initiate(command.tenantId, {
    originalName: command.originalName,
    contentType: command.contentType,
    sizeBytes: command.sizeBytes,
    conversationId: command.conversationId,
    uploadedByUserId: command.userId,
  });
  await deps.attachmentReservations.reserve({
    fileId: initiated.fileId,
    tenantId: command.tenantId,
    conversationId: command.conversationId,
    uploadedByUserId: command.userId,
  });
  return Result.ok(initiated);
}
