import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { computeMeetingUniquenessKey, computeSupportUniquenessKey } from '../../src/domain/conversations/uniqueness-key.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { MessageRepository } from '../../src/application/ports/message-repository.js';
import type { UserPermissionsProjectionRepository } from '../../src/application/ports/user-permissions-projection-repository.js';
import { listConversations } from '../../src/application/use-cases/list-conversations.js';
import { setConversationArchived } from '../../src/application/use-cases/set-conversation-archived.js';
import { finalizeEndedMeeting } from '../../src/application/use-cases/finalize-ended-meeting.js';
import { getMeetingChatHistory } from '../../src/application/use-cases/get-meeting-chat-history.js';
import { authorizeMeetingChatSend } from '../../src/application/use-cases/meeting-chat-send-policy.js';
import { ChatSocketEvents } from '../../src/contracts/socket/chat-socket-events.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';
import { buildMeetingRuntime } from '../helpers/fake-meeting-runtime.js';

const u = () => randomUUID();
const TENANT = 't1';

function meetingChat(meetingId: string, creator = u()) {
  const started = Conversation.startMeetingChat({
    tenantId: TENANT,
    meetingId,
    meetingTitle: 'Review',
    creator: { userId: creator, displayName: 'Host', actorType: 'TenantEmployee' },
  });
  if (!started.isSuccess) throw new Error('meeting chat');
  return started.value;
}

/** Repositorio mínimo en memoria por clave e id, que cuenta los guardados. */
function conversationStore(conversations: Conversation[]) {
  const saved: string[] = [];
  const repo = {
    findById: async (_t: string, id: string) => conversations.find((c) => c.id === id) ?? null,
    findByUniquenessKey: async (_t: string, key: string) => conversations.find((c) => c.toSnapshot().uniquenessKey === key) ?? null,
    save: async (c: Conversation) => void saved.push(c.id),
  } as unknown as ConversationRepository;
  return { repo, saved };
}

function recordingEmitter() {
  const events: Array<{ event: string; payload: unknown }> = [];
  const emitter = {
    emitToConversation: (input: { event: string; envelope: { payload: unknown } }) => events.push({ event: input.event, payload: input.envelope.payload }),
  } as unknown as RealtimeEmitter;
  return { emitter, events };
}

describe('F8a — listado separado por tipo', () => {
  it('pasa los tipos pedidos al repositorio y devuelve los no leídos por tipo', async () => {
    const asked: unknown[] = [];
    const repo = {
      listForUser: async (input: { kinds?: string[] }) => {
        asked.push(input.kinds);
        return [];
      },
      countForUser: async (_t: string, _u: string, _a: boolean, kinds?: string[]) => {
        asked.push(kinds);
        return 0;
      },
      countUnreadByKind: async () => ({ Direct: 2, Group: 1, Support: 4, Meeting: 0 }),
    } as unknown as ConversationRepository;

    const result = await listConversations({ tenantId: TENANT, userId: 'me', page: 1, size: 20, kinds: ['Direct', 'Group'] }, { conversations: repo });

    expect(asked).toEqual([['Direct', 'Group'], ['Direct', 'Group']]);
    expect(result.isSuccess && result.value.unreadByKind).toEqual({ Direct: 2, Group: 1, Support: 4, Meeting: 0 });
  });
});

describe('F8a — archivado (D-01, D-13)', () => {
  it('archivar deja el chat en solo lectura, avisa con chat.conversation.updated y es idempotente', async () => {
    const chat = meetingChat(u());
    const { repo, saved } = conversationStore([chat]);
    const { emitter, events } = recordingEmitter();

    const first = await setConversationArchived({ tenantId: TENANT, archived: true, conversationId: chat.id }, { conversations: repo, emitter });
    const again = await setConversationArchived({ tenantId: TENANT, archived: true, conversationId: chat.id }, { conversations: repo, emitter });

    expect([first, again]).toEqual([true, false]);
    expect(saved).toEqual([chat.id]);
    expect(events).toEqual([{ event: ChatSocketEvents.ConversationUpdated, payload: { conversationId: chat.id, kind: 'Meeting', isArchived: true } }]);
    const creator = chat.getParticipantSnapshots()[0]?.userId ?? '';
    const write = chat.ensureCanWrite(creator);
    expect(!write.isSuccess && write.error.code).toBe('Chat.Conversation.Archived');
  });

  it('reabrir el ticket reactiva su chat (se busca por la clave support:{ticketId})', async () => {
    const ticketId = u();
    const started = Conversation.startSupport({
      tenantId: TENANT,
      ticketId,
      agent: { userId: u(), displayName: 'Agent', actorType: 'PlatformSupport' },
      customer: { userId: u(), displayName: 'Client', actorType: 'CustomerPortal' },
    });
    if (!started.isSuccess) throw new Error();
    const support = started.value;
    support.archive();
    const { repo } = conversationStore([support]);

    await setConversationArchived({ tenantId: TENANT, archived: false, uniquenessKey: computeSupportUniquenessKey(ticketId) }, { conversations: repo });

    expect(support.isArchived).toBe(false);
    expect(support.supportTicketId).toBe(ticketId);
  });

  it('terminar el meeting archiva el chat de la sala', async () => {
    const runtime = buildMeetingRuntime();
    const host = { userId: u(), displayName: 'Ana' };
    const scheduled = Meeting.schedule({ tenantId: TENANT, title: 'Review', host, requireWaitingRoom: false });
    if (!scheduled.isSuccess) throw new Error();
    const meeting = scheduled.value;
    meeting.start({ hostUserId: host.userId });
    meeting.end({ byUserId: host.userId });
    const chat = meetingChat(meeting.id, host.userId);
    const { repo } = conversationStore([chat]);

    await finalizeEndedMeeting(
      { tenantId: TENANT, correlationId: 'c', snapshot: meeting.toSnapshot(), actorUserId: host.userId },
      { ...runtime.deps, conversations: repo },
    );

    expect(chat.isArchived).toBe(true);
    expect(chat.meetingId).toBe(meeting.id);
    expect(chat.toSnapshot().uniquenessKey).toBe(computeMeetingUniquenessKey(meeting.id));
  });
});

describe('F8a — historial del chat en la ficha (D-01)', () => {
  function setup() {
    const host = { userId: u(), displayName: 'Ana' };
    const scheduled = Meeting.schedule({ tenantId: TENANT, title: 'Review', host, requireWaitingRoom: false });
    if (!scheduled.isSuccess) throw new Error();
    const meeting = scheduled.value;
    meeting.start({ hostUserId: host.userId });
    const guest = u();
    meeting.requestJoin({ userId: guest, displayName: 'Client', actorType: 'CustomerPortal', hasValidInvitation: true, passcodeMatch: null });
    const chat = meetingChat(meeting.id, host.userId);
    const sent = chat.sendText({ senderId: host.userId, body: 'Welcome' });
    if (!sent.isSuccess) throw new Error();
    chat.archive();
    const deps = {
      meetings: { findById: async () => meeting },
      conversations: {
        findByUniquenessKey: async () => chat,
        listMessages: async () => [sent.value.toSnapshot()],
      } as unknown as ConversationRepository,
    };
    return { meeting, deps, guest };
  }

  it('quien participó lee el historial aunque el chat esté archivado', async () => {
    const { meeting, deps, guest } = setup();
    const result = await getMeetingChatHistory({ tenantId: TENANT, meetingId: meeting.id, userId: guest, take: 50 }, deps);
    expect(result.isSuccess && result.value).toMatchObject({ isArchived: true, items: [{ body: 'Welcome' }] });
  });

  it('quien no participó recibe Meeting.NotParticipant', async () => {
    const { meeting, deps } = setup();
    const result = await getMeetingChatHistory({ tenantId: TENANT, meetingId: meeting.id, userId: u(), take: 50 }, deps);
    expect(!result.isSuccess && result.error.code).toBe('Meeting.NotParticipant');
  });
});

describe('F8a — reglas del chat de la sala (INV-10 H-7, D-14, D-15)', () => {
  function deps(options: { permissions?: string[]; sent?: number; allow?: boolean } = {}) {
    const userPermissions = {
      findByUserId: async (userId: string) => ({
        userId,
        tenantId: TENANT,
        actorType: 'TenantEmployee',
        permissions: options.permissions ?? ['communication.chat.reply'],
        permissionVersion: 1,
        updatedAtUtc: new Date(),
      }),
    } as unknown as UserPermissionsProjectionRepository;
    return {
      userPermissions,
      effectiveSettings: new FakeEffectiveSettingsResolver({ meetingChatAttachmentsPerMeeting: 3 }),
      rateLimiter: { allow: async () => options.allow ?? true },
      messages: { countAttachmentsBySender: async () => options.sent ?? 0 } as unknown as MessageRepository,
    };
  }
  const principal = (actorType: string, userId = u()) => ({ userId, tenantId: TENANT, actorType, permissionVersion: 1 });
  const code = (r: { isSuccess: boolean; error?: { code: string } }) => (r.isSuccess ? 'ok' : r.error?.code);

  it('guest: texto sí, adjunto no', async () => {
    expect(code(await authorizeMeetingChatSend({ principal: principal('Guest'), conversationId: 'c', hasAttachment: false }, deps()))).toBe('ok');
    expect(code(await authorizeMeetingChatSend({ principal: principal('Guest'), conversationId: 'c', hasAttachment: true }, deps()))).toBe(
      'Meeting.Chat.GuestTextOnly',
    );
  });

  it('cuenta sin el permiso del chat no escribe en la sala', async () => {
    const result = await authorizeMeetingChatSend(
      { principal: principal('TenantEmployee'), conversationId: 'c', hasAttachment: false },
      deps({ permissions: ['communication.meeting.join'] }),
    );
    expect(code(result)).toBe('Auth.Forbidden');
  });

  it('adjuntos: tope por meeting y por minuto desde la configuración', async () => {
    expect(code(await authorizeMeetingChatSend({ principal: principal('TenantEmployee'), conversationId: 'c', hasAttachment: true }, deps({ sent: 3 })))).toBe(
      'Meeting.Chat.AttachmentLimit',
    );
    expect(code(await authorizeMeetingChatSend({ principal: principal('TenantEmployee'), conversationId: 'c', hasAttachment: true }, deps({ allow: false })))).toBe(
      'Meeting.Chat.AttachmentRateLimited',
    );
    expect(code(await authorizeMeetingChatSend({ principal: principal('TenantEmployee'), conversationId: 'c', hasAttachment: true }, deps({ sent: 1 })))).toBe('ok');
  });
});
