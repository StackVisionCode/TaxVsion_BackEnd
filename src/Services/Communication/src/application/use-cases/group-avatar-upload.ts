import { Result, makeError } from '../../domain/shared/result.js';
import { isGroupAdminRole } from '../../domain/conversations/conversation-participant.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { CloudStorageInitiatedUpload, CloudStorageUploadClient } from '../ports/cloudstorage-upload-client.js';

/**
 * F8b (T-21): foto del grupo. La carpeta `Avatars` de CloudStorage solo acepta subidas de servicios, así que
 * Communication media la subida (como los adjuntos del portal): pide la URL presignada, el navegador sube a
 * MinIO y luego se confirma. Solo quien administra el grupo; el tipo y el tamaño los valida CloudStorage.
 */
const IMAGE_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);

interface GroupAvatarActor {
  readonly tenantId: string;
  readonly userId: string;
  readonly canAdministerAnyGroup: boolean;
  readonly conversationId: string;
}

interface GroupAvatarDeps {
  readonly conversations: Pick<ConversationRepository, 'findById'>;
  readonly cloudStorageUpload: CloudStorageUploadClient;
}

async function authorizeGroupAdmin(actor: GroupAvatarActor, deps: GroupAvatarDeps): Promise<Result<void>> {
  const conversation = await deps.conversations.findById(actor.tenantId, actor.conversationId, 0);
  if (!conversation || conversation.kind !== 'Group') {
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }
  if (conversation.isArchived) return Result.fail(makeError('Chat.Conversation.Archived', 'Conversation is archived.'));
  if (!actor.canAdministerAnyGroup && !isGroupAdminRole(conversation.roleOf(actor.userId))) {
    return Result.fail(makeError('Chat.Group.AdminOnly', 'Only group admins can do this.'));
  }
  return Result.okVoid();
}

export async function createGroupAvatarUpload(
  command: GroupAvatarActor & { originalName: string; contentType: string; sizeBytes: number },
  deps: GroupAvatarDeps,
): Promise<Result<CloudStorageInitiatedUpload>> {
  if (!IMAGE_TYPES.has(command.contentType.toLowerCase())) {
    return Result.fail(makeError('Chat.Group.AvatarType', 'Use a JPG, PNG or WebP image.'));
  }
  const authorized = await authorizeGroupAdmin(command, deps);
  if (!authorized.isSuccess) return Result.fail(authorized.error);
  return Result.ok(
    await deps.cloudStorageUpload.initiate(command.tenantId, {
      originalName: command.originalName,
      contentType: command.contentType,
      sizeBytes: command.sizeBytes,
      conversationId: command.conversationId,
      purpose: 'GroupAvatar',
      uploadedByUserId: command.userId,
    }),
  );
}

export async function completeGroupAvatarUpload(
  command: GroupAvatarActor & { fileId: string },
  deps: GroupAvatarDeps,
): Promise<Result<{ fileId: string; status: string }>> {
  const authorized = await authorizeGroupAdmin(command, deps);
  if (!authorized.isSuccess) return Result.fail(authorized.error);
  const completed = await deps.cloudStorageUpload.complete(command.tenantId, command.fileId);
  return Result.ok({ fileId: command.fileId, status: completed.status });
}
