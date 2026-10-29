import { describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { listConversations } from '../../src/application/use-cases/list-conversations.js';
import { getMessages } from '../../src/application/use-cases/get-messages.js';
import { rejoinMeeting } from '../../src/application/use-cases/rejoin-meeting.js';
import { participantSnapshotToDto } from '../../src/application/use-cases/meeting-mappers.js';
import { searchEmployeeDirectory } from '../../src/application/use-cases/search-employee-directory.js';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import type { AvatarResolver } from '../../src/application/use-cases/resolve-avatars.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { MessageRepository } from '../../src/application/ports/message-repository.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { UserDirectoryRepository } from '../../src/application/ports/user-directory-repository.js';
import type { ConversationSnapshot } from '../../src/domain/conversations/conversation.js';
import type { ConversationParticipantSnapshot } from '../../src/domain/conversations/conversation-participant.js';
import type { MessageSnapshot } from '../../src/domain/conversations/message.js';

/**
 * F5 avatares — la foto se agrega al LEER (sin columnas nuevas en participantes/mensajes), con una sola
 * consulta por respuesta. Sin resolver, el campo se omite (no `null`): el front conserva lo que tenia.
 */
const TENANT = 'tenant-1';
const ME = 'user-me';
const PREPARER = 'user-preparer';
const CLIENT = 'user-client';

function resolverWith(
  map: Record<string, string | null>,
): AvatarResolver & { resolve: ReturnType<typeof vi.fn> } {
  return {
    resolve: vi.fn(async () => ({ of: (userId: string) => map[userId] ?? null })),
  };
}

function participant(
  userId: string,
  overrides: Partial<ConversationParticipantSnapshot> = {},
): ConversationParticipantSnapshot {
  return {
    id: randomUUID(),
    conversationId: 'conv-1',
    tenantId: TENANT,
    userId,
    displayName: userId,
    actorType: 'TenantEmployee',
    role: 'Member',
    isPrimaryPreparer: false,
    isMuted: false,
    isRemoved: false,
    joinedAtUtc: new Date(),
    removedAtUtc: null,
    lastReadAtUtc: null,
    lastReadMessageId: null,
    ...overrides,
  };
}

function conversation(id: string, participants: ConversationParticipantSnapshot[]): ConversationSnapshot {
  return {
    id,
    tenantId: TENANT,
    kind: 'Direct',
    title: null,
    uniquenessKey: id,
    isArchived: false,
    lastMessageAtUtc: null,
    createdByUserId: ME,
    createdAtUtc: new Date(),
    updatedAtUtc: new Date(),
    participants,
    recentMessages: [],
  };
}

function conversationsRepo(snapshots: ConversationSnapshot[]): ConversationRepository {
  return {
    listForUser: async () => snapshots,
    countForUser: async () => snapshots.length,
    countUnreadForUser: async () => 0,
  } as unknown as ConversationRepository;
}

describe('listConversations — avatarFileId de los participantes', () => {
  it('resuelve todos los participantes de la pagina en una sola llamada', async () => {
    const snapshots = [
      conversation('c-1', [participant(ME), participant(PREPARER, { isPrimaryPreparer: true })]),
      conversation('c-2', [participant(ME), participant(CLIENT, { actorType: 'CustomerPortal' })]),
    ];
    const avatars = resolverWith({ [PREPARER]: 'f-prep', [CLIENT]: 'f-client' });

    const result = await listConversations(
      { tenantId: TENANT, userId: ME, page: 1, size: 20 },
      { conversations: conversationsRepo(snapshots), avatars },
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(avatars.resolve).toHaveBeenCalledTimes(1);
    expect(avatars.resolve.mock.calls[0]![0]).toEqual([ME, PREPARER, ME, CLIENT]);
    const [first, second] = result.value.items;
    expect(first!.participants).toEqual([
      { userId: ME, displayName: ME, isPrimaryPreparer: false, avatarFileId: null },
      { userId: PREPARER, displayName: PREPARER, isPrimaryPreparer: true, avatarFileId: 'f-prep' },
    ]);
    expect(second!.participants.find((p) => p.userId === CLIENT)?.avatarFileId).toBe('f-client');
  });

  it('no resuelve a los participantes removidos', async () => {
    const avatars = resolverWith({});
    await listConversations(
      { tenantId: TENANT, userId: ME, page: 1, size: 20 },
      {
        conversations: conversationsRepo([
          conversation('c-1', [participant(ME), participant(PREPARER, { isRemoved: true })]),
        ]),
        avatars,
      },
    );
    expect(avatars.resolve.mock.calls[0]![0]).toEqual([ME]);
  });

  it('sin resolver omite el campo', async () => {
    const result = await listConversations(
      { tenantId: TENANT, userId: ME, page: 1, size: 20 },
      { conversations: conversationsRepo([conversation('c-1', [participant(ME)])]) },
    );
    expect(result.isSuccess && 'avatarFileId' in result.value.items[0]!.participants[0]!).toBe(false);
  });
});

function message(id: string, senderId: string): MessageSnapshot {
  return {
    id,
    conversationId: 'conv-1',
    tenantId: TENANT,
    senderId,
    senderDisplayName: senderId,
    kind: 'Text',
    body: 'hola',
    attachmentFileId: null,
    replyToMessageId: null,
    forwardedFromMessageId: null,
    isEdited: false,
    isDeleted: false,
    isPinned: false,
    pinnedAtUtc: null,
    pinnedByUserId: null,
    deletedAtUtc: null,
    createdAtUtc: new Date(),
    editedAtUtc: null,
    audioDurationMs: null,
    audioWaveform: null,
  };
}

describe('getMessages — senderAvatarFileId en el historial', () => {
  it('agrega la foto de cada remitente con una sola consulta', async () => {
    const conversations = {
      findById: async () => ({ isParticipant: () => true }),
      listMessages: async () => [message('m-1', PREPARER), message('m-2', ME), message('m-3', PREPARER)],
    } as unknown as ConversationRepository;
    const messages = { receiptsForOwnMessages: async () => new Map() } as unknown as MessageRepository;
    const avatars = resolverWith({ [PREPARER]: 'f-prep' });

    const result = await getMessages(
      { tenantId: TENANT, conversationId: 'conv-1', requesterUserId: ME, take: 50 },
      { conversations, messages, avatars },
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(avatars.resolve).toHaveBeenCalledTimes(1);
    expect(result.value.items.map((m) => m.senderAvatarFileId)).toEqual(['f-prep', null, 'f-prep']);
  });
});

describe('meetings — avatarFileId en el snapshot', () => {
  it('rejoin devuelve la foto de cada participante', async () => {
    const host = { userId: randomUUID(), displayName: 'Host' };
    const attendee = randomUUID();
    const scheduled = Meeting.schedule({ tenantId: TENANT, title: 'Consulta', host });
    if (!scheduled.isSuccess) throw new Error('schedule failed');
    const meeting = scheduled.value;
    meeting.start({ hostUserId: host.userId });
    meeting.requestJoin({
      userId: attendee,
      displayName: 'Cliente',
      hasValidInvitation: false,
      passcodeMatch: null,
    });
    meeting.admit({ hostUserId: host.userId, targetUserId: attendee });
    const avatars = resolverWith({ [host.userId]: 'f-host' });

    const result = await rejoinMeeting(
      { tenantId: TENANT, meetingId: meeting.id, userId: attendee },
      { meetings: { findById: async () => meeting } as unknown as MeetingRepository, avatars },
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    const byUser = new Map(result.value.snapshot.participants.map((p) => [p.userId, p.avatarFileId]));
    expect(byUser.get(host.userId)).toBe('f-host');
    expect(byUser.get(attendee)).toBeNull();
  });

  it('participantSnapshotToDto sin lookup no manda avatarFileId (un cambio de media no borra la foto)', () => {
    const scheduled = Meeting.schedule({
      tenantId: TENANT,
      title: 'x',
      host: { userId: randomUUID(), displayName: 'Host' },
    });
    if (!scheduled.isSuccess) throw new Error('schedule failed');
    const dto = participantSnapshotToDto(scheduled.value.toSnapshot().participants[0]!);
    expect('avatarFileId' in dto).toBe(false);
  });
});

describe('directorio de empleados — avatarFileId', () => {
  it('el resultado del autocomplete trae la foto de la proyeccion', async () => {
    const userDirectory = {
      searchByDisplayNameOrEmail: async () => [
        {
          userId: PREPARER,
          tenantId: TENANT,
          displayName: 'Prep',
          email: 'prep@x.test',
          isActive: true,
          actorType: 'TenantEmployee',
          avatarFileId: 'f-prep',
          updatedAtUtc: new Date(),
        },
      ],
    } as unknown as UserDirectoryRepository;

    const results = await searchEmployeeDirectory({ tenantId: TENANT, query: 'pre' }, { userDirectory });

    expect(results[0]!.avatarFileId).toBe('f-prep');
  });
});
