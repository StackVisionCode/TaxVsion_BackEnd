import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import type { ConversationParticipantSnapshot } from '../../src/domain/conversations/conversation-participant.js';
import type { ConversationKind } from '../../src/domain/conversations/conversation-kind.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type {
  ChatAttachmentRow,
  ChatScopeRow,
  ConversationClientRow,
  FileLibraryReadRepository,
  RedeclareChatRow,
  RecordingClientRow,
  RecordingFileRow,
  RecordingKind,
  RecordingScopeRow,
  RedeclareRecordingRow,
  RedeclareTranscriptRow,
} from '../../src/application/ports/file-library-read-repository.js';

export interface ParticipantSeed {
  readonly userId: string;
  readonly displayName?: string;
  readonly actorType?: string;
  readonly customerId?: string | null;
  readonly isRemoved?: boolean;
}

/** Conversación rehidratada con los participantes exactos (sin pasar por las factories del aggregate). */
export function conversationWith(input: {
  tenantId: string;
  kind: ConversationKind;
  participants: readonly ParticipantSeed[];
  title?: string | null;
  isArchived?: boolean;
  ticketId?: string;
  meetingId?: string;
  id?: string;
}): Conversation {
  const id = input.id ?? randomUUID();
  const key =
    input.kind === 'Direct'
      ? `direct:${input.participants[0]?.userId}:${input.participants[1]?.userId}`
      : input.kind === 'Group'
        ? `group:${randomUUID()}`
        : input.kind === 'Support'
          ? `support:${input.ticketId ?? randomUUID()}`
          : `meeting:${input.meetingId ?? randomUUID()}`;
  const participants: ConversationParticipantSnapshot[] = input.participants.map((p) => ({
    id: randomUUID(),
    conversationId: id,
    tenantId: input.tenantId,
    userId: p.userId,
    displayName: p.displayName ?? 'User',
    actorType: p.actorType ?? 'TenantEmployee',
    role: 'Member',
    customerId: p.customerId ?? null,
    isPrimaryPreparer: false,
    isMuted: false,
    isRemoved: p.isRemoved ?? false,
    joinedAtUtc: new Date(0),
    removedAtUtc: p.isRemoved ? new Date(0) : null,
    lastReadAtUtc: null,
    lastReadMessageId: null,
  }));
  return Conversation.rehydrate({
    id,
    tenantId: input.tenantId,
    kind: input.kind,
    title: input.title ?? null,
    uniquenessKey: key,
    isArchived: input.isArchived ?? false,
    lastMessageAtUtc: null,
    createdByUserId: input.participants[0]?.userId ?? randomUUID(),
    createdAtUtc: new Date(0),
    updatedAtUtc: new Date(0),
    participants,
    recentMessages: [],
  });
}

/** Repositorio de conversaciones en memoria, con el tenant en la clave (como el de Prisma). */
export function conversationsOf(...conversations: Conversation[]): Pick<ConversationRepository, 'findById'> {
  const byKey = new Map(conversations.map((c) => [`${c.tenantId}:${c.id}`, c]));
  return {
    async findById(tenantId: string, id: string) {
      return byKey.get(`${tenantId}:${id}`) ?? null;
    },
  };
}

/** Lecturas de la biblioteca en memoria: cada prueba carga las filas que devolvería SQL. */
export class FakeFileLibraryReadRepository implements FileLibraryReadRepository {
  memberScope: ChatScopeRow[] = [];
  closedScope: ChatScopeRow[] = [];
  attachments: (ChatAttachmentRow & { tenantId: string })[] = [];
  redeclare: (RedeclareChatRow & { tenantId: string })[] = [];
  clients: (ConversationClientRow & { tenantId: string })[] = [];
  readonly closedCalls: { assignedOnly: boolean }[] = [];
  readonly memberCalls: { userId: string; take: number }[] = [];
  recordingFiles: (RecordingFileRow & { tenantId: string })[] = [];
  callScope: RecordingScopeRow[] = [];
  meetingScope: RecordingScopeRow[] = [];
  readonly callScopeCalls: string[] = [];
  recordingRedeclare: (RedeclareRecordingRow & { tenantId: string })[] = [];
  transcriptRedeclare: (RedeclareTranscriptRow & { tenantId: string })[] = [];
  recordingClients: (RecordingClientRow & { tenantId: string; scope: RecordingKind })[] = [];

  async listMemberChatScope(input: {
    tenantId: string;
    userId: string;
    take: number;
  }): Promise<ChatScopeRow[]> {
    this.memberCalls.push({ userId: input.userId, take: input.take });
    return this.memberScope.slice(0, input.take);
  }

  async listClosedClientChatScope(input: {
    tenantId: string;
    userId: string;
    assignedOnly: boolean;
    take: number;
  }): Promise<ChatScopeRow[]> {
    this.closedCalls.push({ assignedOnly: input.assignedOnly });
    return this.closedScope.slice(0, input.take);
  }

  async findChatAttachments(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<ChatAttachmentRow[]> {
    return this.attachments
      .filter((a) => a.tenantId === input.tenantId && input.fileIds.includes(a.fileId))
      .map(({ fileId, conversationId }) => ({ fileId, conversationId }));
  }

  async listChatAttachmentsForRedeclare(input: {
    tenantId: string;
    afterFileId: string | null;
    take: number;
  }): Promise<RedeclareChatRow[]> {
    return this.redeclare
      .filter(
        (r) => r.tenantId === input.tenantId && (input.afterFileId === null || r.fileId > input.afterFileId),
      )
      .sort((a, b) => (a.fileId < b.fileId ? -1 : a.fileId > b.fileId ? 1 : 0))
      .slice(0, input.take)
      .map(({ fileId, conversationId, isVoiceNote }) => ({ fileId, conversationId, isVoiceNote }));
  }

  async listConversationClients(input: {
    tenantId: string;
    conversationIds: readonly string[];
  }): Promise<ConversationClientRow[]> {
    return this.clients
      .filter((c) => c.tenantId === input.tenantId && input.conversationIds.includes(c.conversationId))
      .map(({ conversationId, actorType, customerId, isRemoved }) => ({
        conversationId,
        actorType,
        customerId,
        isRemoved,
      }));
  }

  async findRecordingFiles(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RecordingFileRow[]> {
    return this.recordingFiles
      .filter((r) => r.tenantId === input.tenantId && input.fileIds.includes(r.fileId))
      .map(({ tenantId: _t, ...row }) => row);
  }

  async listCallRecordingScope(input: {
    tenantId: string;
    userId: string;
    clientCalls: 'none' | 'all' | 'assigned';
    take: number;
  }): Promise<RecordingScopeRow[]> {
    this.callScopeCalls.push(input.clientCalls);
    return this.callScope.slice(0, input.take);
  }

  async listMeetingRecordingScope(input: {
    tenantId: string;
    userId: string;
    take: number;
  }): Promise<RecordingScopeRow[]> {
    return this.meetingScope.slice(0, input.take);
  }

  async listRecordingsForRedeclare(input: {
    tenantId: string;
    afterId: string | null;
    take: number;
  }): Promise<RedeclareRecordingRow[]> {
    return page(this.recordingRedeclare, input);
  }

  async listTranscriptsForRedeclare(input: {
    tenantId: string;
    afterId: string | null;
    take: number;
  }): Promise<RedeclareTranscriptRow[]> {
    return page(this.transcriptRedeclare, input);
  }

  async findChatAttachmentsForRedeclare(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RedeclareChatRow[]> {
    return this.redeclare
      .filter((r) => r.tenantId === input.tenantId && input.fileIds.includes(r.fileId))
      .map(({ fileId, conversationId, isVoiceNote }) => ({ fileId, conversationId, isVoiceNote }));
  }

  async findRecordingsForRedeclare(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RedeclareRecordingRow[]> {
    return this.recordingRedeclare
      .filter((r) => r.tenantId === input.tenantId && input.fileIds.includes(r.fileId))
      .map(({ tenantId: _t, ...row }) => row);
  }

  async findTranscriptsForRedeclare(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RedeclareTranscriptRow[]> {
    const has = (id: string | null) => id !== null && input.fileIds.includes(id);
    return this.transcriptRedeclare
      .filter((r) => r.tenantId === input.tenantId && (has(r.txtFileId) || has(r.jsonFileId)))
      .map(({ tenantId: _t, ...row }) => row);
  }

  async listRecordingClients(input: {
    tenantId: string;
    scope: RecordingKind;
    scopeIds: readonly string[];
  }): Promise<RecordingClientRow[]> {
    return this.recordingClients
      .filter(
        (c) => c.tenantId === input.tenantId && c.scope === input.scope && input.scopeIds.includes(c.scopeId),
      )
      .map(({ scopeId, actorType, customerId }) => ({ scopeId, actorType, customerId }));
  }
}

function page<T extends { tenantId: string; id: string }>(
  rows: readonly T[],
  input: { tenantId: string; afterId: string | null; take: number },
): Omit<T, 'tenantId'>[] {
  return rows
    .filter((r) => r.tenantId === input.tenantId && (input.afterId === null || r.id > input.afterId))
    .sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0))
    .slice(0, input.take)
    .map(({ tenantId: _t, ...row }) => row);
}
