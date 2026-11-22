import { ConversationKind } from '../../domain/conversations/conversation-kind.js';

export interface ChatPushRecipientsInput {
  readonly conversationKind: ConversationKind;
  readonly recipientUserIds: readonly string[];
  readonly mutedUserIds: readonly string[];
  /** Tienen esa conversación abierta y visible al enviar. */
  readonly viewingUserIds?: readonly string[];
  /** Tienen la app visible en otra pantalla al enviar. */
  readonly activeUserIds?: readonly string[];
  /** D-N30: con la app visible el aviso es la insignia; apagable por configuración. */
  readonly suppressWhenActive?: boolean;
}

/**
 * A quién conviene mandar push de un mensaje de chat (R-25: solo viaja esta lista, no la presencia).
 * Orden: reunión → nadie · silenciado · viendo la conversación · app visible (si se suprime).
 * Caso aceptado: con el CRM visible no hay push al móvil (regla de Slack y Teams).
 */
export function chatPushRecipients(input: ChatPushRecipientsInput): string[] {
  if (input.conversationKind === ConversationKind.Meeting) return [];
  const excluded = new Set([
    ...input.mutedUserIds,
    ...(input.viewingUserIds ?? []),
    ...((input.suppressWhenActive ?? true) ? (input.activeUserIds ?? []) : []),
  ]);
  return input.recipientUserIds.filter((userId) => !excluded.has(userId));
}
