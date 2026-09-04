import { Result, makeError } from '../../domain/shared/result.js';
import { SupportStatus } from '../../domain/support/support-enums.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { MessageDto } from '../../contracts/socket/chat-socket-events.js';
import { sendMessage, type SendMessageDeps } from './send-message.js';

/**
 * Envía un mensaje del AGENTE en el chat de un ticket de soporte. La conversación Support vive en el
 * tenant del CLIENTE y el agente real no es participante (lo es el placeholder "Support Team", cuyo
 * userId = id del tenant Platform). Por eso el agente NO puede usar `chat.message.send`. Aquí:
 *   1. Resolvemos el ticket y autorizamos (agente asignado o PlatformAdmin; ticket no cerrado).
 *   2. Delegamos en `sendMessage` con tenant = tenant del cliente y senderUserId = placeholder, así el
 *      mensaje pasa `isParticipant`, sale como "Support Team" (anonimiza al agente) y reusa toda la
 *      tubería de chat (idempotencia, adjuntos, evento MessageSent, cotejos).
 *
 * Devolvemos también `customerTenantId` (donde vive la conversación) para que el handler emita el
 * `chat.message.new` a la sala correcta `t:{customerTenantId}:c:{conversationId}`.
 */
export interface SendSupportAgentMessageCommand {
  readonly correlationId: string;
  readonly clientKey: string;
  readonly ticketId: string;
  readonly agent: { userId: string; tenantId: string; isPlatformAdmin: boolean };
  readonly body?: string | undefined;
  readonly attachmentFileId?: string | undefined;
  readonly replyToMessageId?: string | undefined;
  readonly audioDurationMs?: number | undefined;
  readonly audioWaveform?: number[] | undefined;
}

export interface SendSupportAgentMessageResult {
  readonly message: MessageDto;
  readonly conversationId: string;
  readonly customerTenantId: string;
  readonly recipientUserIds: readonly string[];
}

export interface SendSupportAgentMessageDeps extends SendMessageDeps {
  readonly supportTickets: SupportTicketRepository;
}

export async function sendSupportAgentMessage(
  cmd: SendSupportAgentMessageCommand,
  deps: SendSupportAgentMessageDeps,
): Promise<Result<SendSupportAgentMessageResult>> {
  const ticket = await deps.supportTickets.findById(cmd.ticketId);
  if (!ticket) {
    return Result.fail(makeError('Support.NotFound', 'Support ticket not found.'));
  }
  if (ticket.status === SupportStatus.Closed) {
    return Result.fail(makeError('Support.Terminal', 'Cannot message a closed ticket; reopen it first.'));
  }
  // Autorización de ENVÍO: hay que haber reclamado (ser el agente asignado), salvo PlatformAdmin. Un
  // agente con permiso que aún no reclamó puede LEER/mirar el ticket, pero no responder como soporte.
  const isAssignedAgent =
    cmd.agent.userId === ticket.assignedAgentId && cmd.agent.tenantId === ticket.agentTenantId;
  if (!isAssignedAgent && !cmd.agent.isPlatformAdmin) {
    return Result.fail(makeError('Auth.Forbidden', 'Claim the ticket before replying as support.'));
  }

  const send = await sendMessage(
    {
      // La conversación vive en el tenant del cliente; el placeholder es el emisor (participante válido).
      tenantId: ticket.tenantId,
      correlationId: cmd.correlationId,
      clientKey: cmd.clientKey,
      conversationId: ticket.conversationId,
      senderUserId: ticket.agentTenantId,
      body: cmd.body,
      attachmentFileId: cmd.attachmentFileId,
      replyToMessageId: cmd.replyToMessageId,
      audioDurationMs: cmd.audioDurationMs,
      audioWaveform: cmd.audioWaveform,
    },
    deps,
  );
  if (!send.isSuccess) {
    return Result.fail(send.error);
  }

  return Result.ok({
    message: send.value.message,
    conversationId: ticket.conversationId,
    customerTenantId: ticket.tenantId,
    recipientUserIds: send.value.recipientUserIds,
  });
}
