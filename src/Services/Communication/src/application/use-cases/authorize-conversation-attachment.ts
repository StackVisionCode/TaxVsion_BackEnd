import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type {
  AttachmentTrackingRepository,
  AttachmentTrackingSnapshot,
} from '../ports/attachment-tracking-repository.js';

/**
 * Decision unica de acceso a un adjunto de chat: el archivo existe, pertenece a
 * la conversacion indicada y el caller es participante activo de ella.
 *
 * Communication es la autoridad de membresia — CloudStorage solo sabe de duenos
 * (`ownerType`), y un adjunto de chat es `ownerType=Communication`, invisible al
 * scope por-dueno de un CustomerPortal. Aca esta la unica frontera de acceso; la
 * emision del presigned se delega a CloudStorage como actor `Service`.
 *
 * Devuelve el tracking (con su Status de escaneo) para que cada caller aplique su
 * propia politica sobre el estado (metadata lo muestra tal cual; download-url exige
 * `Available`). Reutilizable para cualquier blob de Communication accedido por
 * participantes (recordings/transcripts/support), no solo adjuntos de chat.
 */
export interface AuthorizeConversationAttachmentCommand {
  readonly tenantId: string;
  readonly userId: string;
  readonly conversationId: string;
  readonly fileId: string;
}

export interface AuthorizeConversationAttachmentDeps {
  readonly conversations: ConversationRepository;
  readonly attachmentTracking: AttachmentTrackingRepository;
}

export async function authorizeConversationAttachment(
  command: AuthorizeConversationAttachmentCommand,
  deps: AuthorizeConversationAttachmentDeps,
): Promise<Result<AttachmentTrackingSnapshot>> {
  const tracking = await deps.attachmentTracking.findByFileId(command.fileId);
  // Respuesta uniforme (NotFound) para archivo inexistente, de otro tenant o de
  // otra conversacion: nunca revelamos la existencia de un adjunto que el caller
  // no tiene derecho a ver (anti-enumeracion).
  if (
    !tracking ||
    tracking.tenantId !== command.tenantId ||
    tracking.conversationId !== command.conversationId
  ) {
    return Result.fail(makeError('Chat.Attachment.NotFound', 'Attachment not found.'));
  }

  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId, 0);
  if (!conversation) {
    return Result.fail(makeError('Chat.Attachment.NotFound', 'Attachment not found.'));
  }
  if (!conversation.isParticipant(command.userId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }

  return Result.ok(tracking);
}
