import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { Conversation } from '../../domain/conversations/conversation.js';
import { computeMeetingUniquenessKey } from '../../domain/conversations/uniqueness-key.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { ChatEventTypes, type ConversationStartedEvent } from '../../contracts/events/chat-events.js';

/**
 * Idempotente: crea el chat del meeting si es la primera persona en entrar,
 * o agrega al que se une a un chat que ya existe. Llamada desde
 * `join-meeting.ts` (join directo) y `admitParticipant` (sale de la sala de
 * espera) — nunca desde `Meeting.schedule` (agendar no implica que el chat
 * exista todavia, evita crear conversaciones de meetings que nunca arrancan).
 *
 * `AlreadyParticipant` se trata como exito — un reconnect (mismo socket
 * vuelve a mandar Join) no debe fallar.
 */
export interface EnsureMeetingConversationCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly meetingTitle: string;
  readonly member: { userId: string; displayName: string; actorType: string };
}

export async function ensureMeetingConversation(
  command: EnsureMeetingConversationCommand,
  deps: { conversations: ConversationRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ conversationId: string }>> {
  const uniquenessKey = computeMeetingUniquenessKey(command.meetingId);
  const existing = await deps.conversations.findByUniquenessKey(command.tenantId, uniquenessKey);

  if (!existing) {
    const created = Conversation.startMeetingChat({
      tenantId: command.tenantId,
      meetingId: command.meetingId,
      meetingTitle: command.meetingTitle,
      creator: command.member,
    });
    if (!created.isSuccess) return Result.fail(created.error);
    await deps.conversations.save(created.value);

    const event: ConversationStartedEvent = {
      eventId: randomUUID(),
      eventType: ChatEventTypes.ConversationStarted,
      tenantId: command.tenantId,
      correlationId: command.correlationId,
      occurredOnUtc: new Date().toISOString(),
      conversationId: created.value.id,
      kind: 'Meeting',
      createdByUserId: command.member.userId,
      participantUserIds: [command.member.userId],
    };
    await deps.publisher.enqueue(event);

    return Result.ok({ conversationId: created.value.id });
  }

  const addResult = existing.addParticipant({
    actorUserId: command.member.userId,
    newMember: command.member,
  });
  if (!addResult.isSuccess && addResult.error.code !== 'Chat.Conversation.AlreadyParticipant') {
    return Result.fail(addResult.error);
  }
  if (addResult.isSuccess) {
    await deps.conversations.save(existing);
  }

  return Result.ok({ conversationId: existing.id });
}

/**
 * Contraparte de salida — usada en `leave-meeting.ts` y en el kick del host
 * (`removeParticipant` en meeting-host-actions.ts). No falla si el chat del
 * meeting no existe todavia (nadie llego a mandar un mensaje) ni si el user
 * ya no es participante activo — salir dos veces es un no-op, no un error.
 */
export async function removeFromMeetingConversation(
  command: {
    tenantId: string;
    meetingId: string;
    actorUserId: string;
    targetUserId: string;
  },
  deps: { conversations: ConversationRepository },
): Promise<Result<{ conversationId: string; reason: 'Left' | 'Kicked' } | null>> {
  const uniquenessKey = computeMeetingUniquenessKey(command.meetingId);
  const conversation = await deps.conversations.findByUniquenessKey(command.tenantId, uniquenessKey);
  if (!conversation) return Result.ok(null);

  const removeResult = conversation.removeParticipant({
    actorUserId: command.actorUserId,
    targetUserId: command.targetUserId,
  });
  if (!removeResult.isSuccess) {
    // Ya no era participante activo (por ejemplo, nunca llego a entrar al
    // chat porque nunca mando/leyo nada) — no es un error real, es un no-op.
    if (removeResult.error.code === 'Chat.Conversation.NotParticipant') {
      return Result.ok(null);
    }
    return Result.fail(makeError(removeResult.error.code, removeResult.error.message));
  }
  await deps.conversations.save(conversation);
  return Result.ok({ conversationId: conversation.id, reason: removeResult.value.reason });
}
