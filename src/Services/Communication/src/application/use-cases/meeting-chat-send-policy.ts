import { Result, makeError } from '../../domain/shared/result.js';
import { checkPermission, CommunicationPermissions, type PermissionSubject } from '../../domain/shared/permissions.js';
import { CommunicationRateLimitPolicyNames } from '../../domain/rate-limit/rate-limit-policies.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { MessageRepository } from '../ports/message-repository.js';

/** Ventana del tope de adjuntos por minuto (D-15); el número vive en la configuración. */
const ATTACHMENT_WINDOW_SECONDS = 60;

export interface MeetingChatSendCheck {
  readonly principal: PermissionSubject & { readonly tenantId: string; readonly actorType: string };
  readonly conversationId: string;
  readonly hasAttachment: boolean;
}

export interface MeetingChatSendPolicyDeps {
  readonly userPermissions: UserPermissionsProjectionRepository;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  readonly rateLimiter: {
    allow(input: { scope: string; tenantId: string; userId: string; maxPerWindow: number; windowSeconds: number }): Promise<boolean>;
  };
  readonly messages: Pick<MessageRepository, 'countAttachmentsBySender'>;
}

/**
 * F8a — reglas del chat de la sala antes de enviar:
 * - Cuentas: el mismo permiso que el chat (`chat.message.send` → ChatReply), INV-10 H-7.
 * - Guests: solo texto (D-14); no tienen permisos, entraron por su invitación.
 * - Adjuntos (D-15): tope por minuto con scope propio y tope por usuario y meeting, ambos de la configuración.
 */
export async function authorizeMeetingChatSend(
  check: MeetingChatSendCheck,
  deps: MeetingChatSendPolicyDeps,
): Promise<Result<void>> {
  const { principal } = check;
  const isGuest = principal.actorType === 'Guest';
  if (isGuest) {
    if (check.hasAttachment) {
      return Result.fail(makeError('Meeting.Chat.GuestTextOnly', 'Guests can only send text messages.'));
    }
    return Result.okVoid();
  }

  const permission = await checkPermission(principal, CommunicationPermissions.ChatReply, deps.userPermissions);
  if (!permission.allowed) return Result.fail(makeError(permission.code, permission.message));
  if (!check.hasAttachment) return Result.okVoid();

  const meeting = (await deps.effectiveSettings.resolve(principal.tenantId)).meeting;
  const sent = await deps.messages.countAttachmentsBySender(principal.tenantId, check.conversationId, principal.userId);
  if (sent >= meeting.chatAttachmentsPerMeeting) {
    return Result.fail(makeError('Meeting.Chat.AttachmentLimit', 'You reached the file limit for this meeting.'));
  }
  const allowed = await deps.rateLimiter.allow({
    scope: CommunicationRateLimitPolicyNames.MeetingChatAttachment,
    tenantId: principal.tenantId,
    userId: principal.userId,
    maxPerWindow: meeting.chatAttachmentsPerMinute,
    windowSeconds: ATTACHMENT_WINDOW_SECONDS,
  });
  if (!allowed) return Result.fail(makeError('Meeting.Chat.AttachmentRateLimited', 'Too many files, wait a moment.'));
  return Result.okVoid();
}
