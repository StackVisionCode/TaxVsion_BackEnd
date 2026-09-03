import { Result } from '../../domain/shared/result.js';
import {
  authorizeConversationParticipant,
  type AuthorizeConversationParticipantDeps,
} from './authorize-conversation-participant.js';
import type { CloudStorageUploadClient } from '../ports/cloudstorage-upload-client.js';

/**
 * Media la FINALIZACION de la subida (verifica tamano real + dispara el escaneo
 * en CloudStorage). Membresia requerida — el blob es `ownerType=Communication`,
 * que el cliente no puede finalizar directo.
 */
export interface CompleteAttachmentUploadCommand {
  readonly tenantId: string;
  readonly userId: string;
  readonly conversationId: string;
  readonly fileId: string;
}

export interface CompleteAttachmentUploadResult {
  readonly fileId: string;
  readonly status: string;
}

export interface CompleteAttachmentUploadDeps extends AuthorizeConversationParticipantDeps {
  readonly cloudStorageUpload: CloudStorageUploadClient;
}

export async function completeAttachmentUpload(
  command: CompleteAttachmentUploadCommand,
  deps: CompleteAttachmentUploadDeps,
): Promise<Result<CompleteAttachmentUploadResult>> {
  const authorized = await authorizeConversationParticipant(command, deps);
  if (!authorized.isSuccess) return Result.fail(authorized.error);

  const completed = await deps.cloudStorageUpload.complete(command.tenantId, command.fileId);
  return Result.ok({ fileId: command.fileId, status: completed.status });
}
