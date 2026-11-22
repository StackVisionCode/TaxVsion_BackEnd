import { describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type {
  ConversationActivity,
  ConversationActivityReader,
} from '../../src/application/ports/conversation-activity-reader.js';
import { listConversations } from '../../src/application/use-cases/list-conversations.js';

const TENANT = randomUUID();
const ME = randomUUID();
const ANA = randomUUID();

function group(title: string) {
  const started = Conversation.startGroup({
    tenantId: TENANT,
    groupId: randomUUID(),
    title,
    creator: { userId: ME, displayName: 'Me', actorType: 'TenantEmployee' },
    members: [{ userId: ANA, displayName: 'Ana', actorType: 'TenantEmployee' }],
  });
  if (!started.isSuccess) throw new Error(started.error.code);
  return started.value.toSnapshot();
}

/** Repositorio cuyo conteo por fila revienta: con lector no debe llamarse. */
function repoWith(snapshots: ReturnType<typeof group>[]): ConversationRepository {
  return {
    listForUser: async () => snapshots,
    countForUser: async () => snapshots.length,
    countUnreadByKind: async () => ({ Direct: 0, Group: 5, Support: 0, Meeting: 0 }),
    countUnreadForUser: async () => {
      throw new Error('countUnreadForUser por fila');
    },
  } as unknown as ConversationRepository;
}

describe('listConversations — lector de actividad', () => {
  it('usa el lector de actividad una vez y devuelve lastMessage', async () => {
    const busy = group('Busy');
    const quiet = group('Quiet');
    const sentAt = new Date(Date.UTC(2026, 10, 20, 15, 30));
    const activity = new Map<string, ConversationActivity>([
      [
        busy.id,
        {
          unreadCount: 4,
          lastMessage: {
            senderId: ANA,
            senderDisplayName: 'Ana',
            kind: 'Attachment',
            body: null,
            isDeleted: false,
            createdAtUtc: sentAt,
            audioDurationMs: 3200,
          },
        },
      ],
    ]);
    const readForUser = vi.fn<ConversationActivityReader['readForUser']>(async () => activity);

    const result = await listConversations(
      { tenantId: TENANT, userId: ME, page: 1, size: 20 },
      { conversations: repoWith([busy, quiet]), conversationActivity: { readForUser } },
    );

    expect(readForUser).toHaveBeenCalledTimes(1);
    expect(readForUser).toHaveBeenCalledWith({
      tenantId: TENANT,
      userId: ME,
      conversationIds: [busy.id, quiet.id],
      historyFloor: true,
    });
    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    const [first, second] = result.value.items;
    expect(first?.unreadCount).toBe(4);
    expect(first?.lastMessage).toEqual({
      senderId: ANA,
      senderDisplayName: 'Ana',
      kind: 'Attachment',
      preview: null,
      isVoiceNote: true,
      isDeleted: false,
      createdAtUtc: sentAt.toISOString(),
    });
    // Sin actividad: sin no leídos y sin último mensaje.
    expect(second?.unreadCount).toBe(0);
    expect(second?.lastMessage).toBeNull();
  });

  it('sin lector conserva el conteo por fila y no agrega lastMessage', async () => {
    const busy = group('Busy');
    const repo = {
      ...repoWith([busy]),
      countUnreadForUser: async () => 2,
    } as unknown as ConversationRepository;

    const result = await listConversations(
      { tenantId: TENANT, userId: ME, page: 1, size: 20 },
      { conversations: repo },
    );

    const item = result.isSuccess ? result.value.items[0] : undefined;
    expect(item).toMatchObject({ unreadCount: 2 });
    expect(item && 'lastMessage' in item).toBe(false);
  });
});
