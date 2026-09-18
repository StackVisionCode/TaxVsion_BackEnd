import { randomUUID } from 'node:crypto';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { SocketEnvelope } from '../../contracts/socket/socket-envelope.js';
import { ChatSocketEvents } from '../../contracts/socket/chat-socket-events.js';
import type { CallKind } from '../../domain/calls/call-kind.js';
import { messageSnapshotToDto } from './chat-mappers.js';

export interface CallSystemMessageDeps {
  readonly conversations: ConversationRepository;
  readonly emitter: RealtimeEmitter;
}

/**
 * Escribe y EMITE un mensaje System (evento de llamada) en la conversación de la llamada, para que quede
 * en el historial del chat de ambos lados (estilo WhatsApp: "Missed call" / "Call · 2:34"). Best-effort:
 * si la conversación no existe o el append falla, no rompe el flujo de la llamada.
 */
export async function appendCallSystemMessage(
  input: { tenantId: string; conversationId: string; body: string; now?: Date },
  deps: CallSystemMessageDeps,
): Promise<void> {
  const conversation = await deps.conversations.findById(input.tenantId, input.conversationId, 0);
  if (!conversation) return;
  const messageResult = conversation.appendSystemMessage({
    body: input.body,
    ...(input.now ? { now: input.now } : {}),
  });
  if (!messageResult.isSuccess) return;
  await deps.conversations.save(conversation);
  const envelope: SocketEnvelope<unknown> = {
    eventId: randomUUID(),
    correlationId: '',
    emittedAtUtc: new Date().toISOString(),
    payload: messageSnapshotToDto(messageResult.value.toSnapshot()),
  };
  deps.emitter.emitToConversation({
    tenantId: input.tenantId,
    conversationId: input.conversationId,
    event: ChatSocketEvents.MessageNew,
    envelope,
  });
}

/** "m:ss" a partir de segundos (para el cuerpo "Call · 2:34"). */
export function formatCallDuration(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

/** Cuerpo del mensaje System según el estado terminal de la llamada (copy en inglés, estilo WhatsApp). */
export function callSystemMessageBody(input: {
  status: 'MissedCall' | 'Ended';
  kind: CallKind;
  durationSeconds?: number | null;
}): string {
  const isVideo = input.kind === 'Video';
  if (input.status === 'MissedCall') {
    return isVideo ? 'Missed video call' : 'Missed call';
  }
  const label = isVideo ? 'Video call' : 'Call';
  const seconds = input.durationSeconds ?? 0;
  return seconds > 0 ? `${label} · ${formatCallDuration(seconds)}` : label;
}
