import { Result, makeError } from '../../domain/shared/result.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageRepository } from '../ports/message-repository.js';
import { getMessages, type GetMessagesResult } from './get-messages.js';

/**
 * Historial del chat de un ticket para el AGENTE. La conversación vive en el tenant del cliente y el
 * agente no es participante, así que no puede usar `GET /communication/conversations/:id/messages`
 * (escopa por su tenant). Aquí resolvemos el ticket, autorizamos por `canBeAccessedBy` (más amplio que
 * el envío: un agente con permiso puede LEER un ticket Open aunque no lo haya reclamado) y leemos la
 * conversación cross-tenant COMO el placeholder "Support Team" (que sí es participante).
 */
export interface GetSupportAgentMessagesCommand {
  readonly ticketId: string;
  readonly agent: {
    userId: string;
    tenantId: string;
    hasAgentPermission: boolean;
    isPlatformAdmin: boolean;
  };
  readonly take: number;
  readonly beforeUtc?: string | undefined;
}

export interface GetSupportAgentMessagesDeps {
  readonly supportTickets: SupportTicketRepository;
  readonly conversations: ConversationRepository;
  readonly messages: MessageRepository;
}

export async function getSupportAgentMessages(
  cmd: GetSupportAgentMessagesCommand,
  deps: GetSupportAgentMessagesDeps,
): Promise<Result<GetMessagesResult>> {
  const ticket = await deps.supportTickets.findById(cmd.ticketId);
  if (!ticket) {
    return Result.fail(makeError('Support.NotFound', 'Support ticket not found.'));
  }
  const canAccess = ticket.canBeAccessedBy({
    actorUserId: cmd.agent.userId,
    actorTenantId: cmd.agent.tenantId,
    actorHasAgentPermission: cmd.agent.hasAgentPermission,
    isPlatformAdmin: cmd.agent.isPlatformAdmin,
  });
  if (!canAccess) {
    return Result.fail(makeError('Auth.Forbidden', 'Not allowed to read this support ticket.'));
  }

  const snap = ticket.toSnapshot();
  return getMessages(
    {
      tenantId: snap.tenantId,
      conversationId: snap.conversationId,
      requesterUserId: snap.agentTenantId, // placeholder "Support Team" (participante válido)
      take: cmd.take,
      ...(cmd.beforeUtc !== undefined ? { beforeUtc: cmd.beforeUtc } : {}),
    },
    deps,
  );
}
