import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { appendCallSystemMessage } from '../../src/application/use-cases/append-call-system-message.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import { CloudStorageEventTypes } from '../../src/contracts/events/cloudstorage-events.js';

const u = () => randomUUID();

/**
 * El transcript que la llamada deja en el chat es `call` · `transcript` (lo declara M4 al quedar listo), nunca un
 * adjunto de chat: el mensaje System no publica declaración aunque registre el tracking.
 */
describe('appendCallSystemMessage — el transcript no se declara como chat', () => {
  it('registra el adjunto pero no encola ninguna declaración de origen', async () => {
    const tenantId = u();
    const started = Conversation.startDirect({
      tenantId,
      initiator: { userId: u(), displayName: 'Ana', actorType: 'TenantEmployee' },
      recipient: { userId: u(), displayName: 'Bob', actorType: 'CustomerPortal' },
    });
    if (!started.isSuccess) throw new Error('conversation');
    const registered: string[] = [];
    const published: IntegrationEvent[] = [];
    const deps = {
      conversations: {
        findById: async () => started.value,
        save: async () => undefined,
      } as unknown as ConversationRepository,
      emitter: { emitToConversation: () => undefined } as unknown as RealtimeEmitter,
      attachmentTracking: {
        register: async (input: { fileId: string }) => void registered.push(input.fileId),
        markStatus: async () => null,
        findByFileId: async () => null,
      },
      publisher: { enqueue: async (e: IntegrationEvent) => void published.push(e) },
    };
    const fileId = u();

    await appendCallSystemMessage(
      {
        tenantId,
        conversationId: started.value.id,
        body: 'Call transcript is ready',
        attachmentFileId: fileId,
      },
      deps,
    );

    expect(registered).toEqual([fileId]);
    expect(published.filter((e) => e.eventType === CloudStorageEventTypes.FileSourceDeclared)).toEqual([]);
  });
});
