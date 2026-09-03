import { Result, makeError } from '../../domain/shared/result.js';
import {
  authorizeConversationAttachment,
  type AuthorizeConversationAttachmentCommand,
  type AuthorizeConversationAttachmentDeps,
} from './authorize-conversation-attachment.js';
import type { CloudStorageDownloadClient } from '../ports/cloudstorage-download-client.js';

/**
 * URL de descarga presignada de un adjunto de chat, previa autorizacion de
 * membresia y solo si el escaneo lo dejo `Available`. Estados no descargables se
 * mapean a errores humanos, sin filtrar internals del antivirus.
 */
export type GetAttachmentDownloadUrlCommand = AuthorizeConversationAttachmentCommand;

export interface AttachmentDownloadUrlResult {
  readonly downloadUrl: string;
  readonly expiresAtUtc: string;
}

export interface GetAttachmentDownloadUrlDeps extends AuthorizeConversationAttachmentDeps {
  readonly cloudStorageDownload: CloudStorageDownloadClient;
}

export async function getAttachmentDownloadUrl(
  command: GetAttachmentDownloadUrlCommand,
  deps: GetAttachmentDownloadUrlDeps,
): Promise<Result<AttachmentDownloadUrlResult>> {
  const authorized = await authorizeConversationAttachment(command, deps);
  if (!authorized.isSuccess) return Result.fail(authorized.error);
  const { status } = authorized.value;

  if (status === 'Pending') {
    return Result.fail(makeError('Chat.Attachment.Pending', 'The attachment is still being processed.'));
  }
  if (status === 'Deleted') {
    return Result.fail(makeError('Chat.Attachment.Deleted', 'This attachment is no longer available.'));
  }
  if (status !== 'Available') {
    // Infected | BlockedByPolicy.
    return Result.fail(makeError('Chat.Attachment.Unavailable', 'This attachment was removed by security checks.'));
  }

  const url = await deps.cloudStorageDownload.getDownloadUrl(command.tenantId, command.fileId);
  if (!url) {
    // 404 en CloudStorage pese a estar 'Available' — carrera con un borrado.
    return Result.fail(makeError('Chat.Attachment.Deleted', 'This attachment is no longer available.'));
  }
  return Result.ok({ downloadUrl: url.downloadUrl, expiresAtUtc: url.expiresAtUtc });
}
