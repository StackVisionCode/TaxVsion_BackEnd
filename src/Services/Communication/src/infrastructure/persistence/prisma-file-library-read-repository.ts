import { Prisma, type PrismaClient } from '@prisma/client';
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
} from '../../application/ports/file-library-read-repository.js';

// Hay algún adjunto disponible de un mensaje vivo en la conversación `c`.
const HAS_LIVE_ATTACHMENT = Prisma.sql`EXISTS (
  SELECT 1 FROM [dbo].[AttachmentTracking] a
  JOIN [dbo].[Message] m ON m.[Id] = a.[MessageId] AND m.[TenantId] = a.[TenantId]
  WHERE a.[ConversationId] = c.[Id] AND a.[TenantId] = c.[TenantId] AND a.[Status] = 'Available' AND m.[IsDeleted] = 0)`;

// El `.txt` de un transcript se responde por la rama de grabaciones, nunca como adjunto de chat.
const NOT_A_TRANSCRIPT = Prisma.sql`NOT EXISTS (
  SELECT 1 FROM [dbo].[TranscriptArtifact] t WHERE t.[TenantId] = a.[TenantId] AND t.[TxtFileId] = a.[FileId])`;

// Más activas primero; las que nunca tuvieron mensajes, al final.
const BY_ACTIVITY = Prisma.sql`ORDER BY CASE WHEN c.[LastMessageAtUtc] IS NULL THEN 1 ELSE 0 END, c.[LastMessageAtUtc] DESC, c.[Id]`;

type ScopeRaw = { ConversationId: string; LastActivityAtUtc: Date | null };

// Sesión del transcript: la suya, o la primera grabación si es anterior a D-71 (sin sesión), como el endpoint.
const TRANSCRIPT_SESSION = Prisma.sql`s.[TenantId] = t.[TenantId] AND s.[Scope] = t.[Scope] AND s.[ScopeId] = t.[ScopeId]
  AND (s.[Id] = t.[RecordingSessionId] OR (t.[RecordingSessionId] IS NULL AND s.[Sequence] = 1))`;

// Actividad de una grabación lista: cuándo quedó lista (o, si falta, cuándo se pidió).
const READY_AT = Prisma.sql`MAX(COALESCE(s.[ReadyAtUtc], s.[StoppedAtUtc], s.[RequestedAtUtc]))`;

type RecordingScopeRaw = { ScopeId: string; LastActivityAtUtc: Date | null };

/** Lecturas de la biblioteca de archivos. Consultas crudas; los GUID se normalizan a minúsculas. */
export class PrismaFileLibraryReadRepository implements FileLibraryReadRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async listMemberChatScope(input: {
    tenantId: string;
    userId: string;
    take: number;
  }): Promise<ChatScopeRow[]> {
    const rows = await this.prisma.$queryRaw<ScopeRaw[]>`
      SELECT TOP (${input.take}) c.[Id] AS ConversationId, c.[LastMessageAtUtc] AS LastActivityAtUtc
      FROM [dbo].[ConversationParticipant] p
      JOIN [dbo].[Conversation] c ON c.[Id] = p.[ConversationId] AND c.[TenantId] = p.[TenantId]
      WHERE p.[TenantId] = ${input.tenantId} AND p.[UserId] = ${input.userId} AND p.[IsRemoved] = 0
        AND ${HAS_LIVE_ATTACHMENT}
      ${BY_ACTIVITY}`;
    return rows.map(toScopeRow);
  }

  async listClosedClientChatScope(input: {
    tenantId: string;
    userId: string;
    assignedOnly: boolean;
    take: number;
  }): Promise<ChatScopeRow[]> {
    // Mismo criterio que Conversation.isClosedClientChat: Direct archivado, staff retirado y cliente vigente.
    const assigned = input.assignedOnly
      ? Prisma.sql`AND EXISTS (
          SELECT 1 FROM [dbo].[CustomerAssignmentProjection] cap
          WHERE cap.[TenantId] = c.[TenantId] AND cap.[UserId] = ${input.userId}
            AND cap.[CustomerId] = COALESCE(client.[CustomerId], portal.[CustomerId]))`
      : Prisma.empty;
    const rows = await this.prisma.$queryRaw<ScopeRaw[]>`
      SELECT TOP (${input.take}) c.[Id] AS ConversationId, c.[LastMessageAtUtc] AS LastActivityAtUtc
      FROM [dbo].[Conversation] c
      JOIN [dbo].[ConversationParticipant] client
        ON client.[ConversationId] = c.[Id] AND client.[IsRemoved] = 0 AND client.[ActorType] = 'CustomerPortal'
      OUTER APPLY (
        SELECT TOP (1) cpa.[CustomerId] FROM [dbo].[CustomerPortalAccount] cpa
        WHERE cpa.[UserId] = client.[UserId] AND cpa.[IsActive] = 1) portal
      WHERE c.[TenantId] = ${input.tenantId} AND c.[Kind] = 'Direct' AND c.[IsArchived] = 1
        AND EXISTS (
          SELECT 1 FROM [dbo].[ConversationParticipant] s
          WHERE s.[ConversationId] = c.[Id] AND s.[IsRemoved] = 1
            AND s.[ActorType] IN ('TenantEmployee', 'TenantAdmin', 'PlatformAdmin'))
        AND ${HAS_LIVE_ATTACHMENT}
        ${assigned}
      ${BY_ACTIVITY}`;
    return rows.map(toScopeRow);
  }

  async findChatAttachments(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<ChatAttachmentRow[]> {
    if (input.fileIds.length === 0) return [];
    const rows = await this.prisma.$queryRaw<{ FileId: string; ConversationId: string }[]>`
      SELECT a.[FileId], a.[ConversationId]
      FROM [dbo].[AttachmentTracking] a
      JOIN [dbo].[Message] m ON m.[Id] = a.[MessageId] AND m.[TenantId] = a.[TenantId]
      WHERE a.[TenantId] = ${input.tenantId} AND a.[Status] = 'Available' AND m.[IsDeleted] = 0
        AND a.[FileId] IN (${Prisma.join([...input.fileIds])})
        AND ${NOT_A_TRANSCRIPT}`;
    return rows.map((r) => ({
      fileId: r.FileId.toLowerCase(),
      conversationId: r.ConversationId.toLowerCase(),
    }));
  }

  async listChatAttachmentsForRedeclare(input: {
    tenantId: string;
    afterFileId: string | null;
    take: number;
  }): Promise<RedeclareChatRow[]> {
    const after = input.afterFileId
      ? Prisma.sql`AND a.[FileId] > CAST(${input.afterFileId} AS uniqueidentifier)`
      : Prisma.empty;
    const rows = await this.prisma.$queryRaw<
      { FileId: string; ConversationId: string; IsVoiceNote: number | boolean }[]
    >`
      SELECT TOP (${input.take}) a.[FileId], a.[ConversationId],
             CASE WHEN m.[Kind] = 'Attachment' AND m.[AudioDurationMs] IS NOT NULL THEN 1 ELSE 0 END AS IsVoiceNote
      FROM [dbo].[AttachmentTracking] a
      JOIN [dbo].[Message] m ON m.[Id] = a.[MessageId] AND m.[TenantId] = a.[TenantId]
      WHERE a.[TenantId] = ${input.tenantId} ${after}
        AND ${NOT_A_TRANSCRIPT}
      ORDER BY a.[FileId]`;
    return rows.map((r) => ({
      fileId: r.FileId.toLowerCase(),
      conversationId: r.ConversationId.toLowerCase(),
      isVoiceNote: Number(r.IsVoiceNote) === 1,
    }));
  }

  async findChatAttachmentsForRedeclare(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RedeclareChatRow[]> {
    if (input.fileIds.length === 0) return [];
    const rows = await this.prisma.$queryRaw<
      { FileId: string; ConversationId: string; IsVoiceNote: number | boolean }[]
    >`
      SELECT a.[FileId], a.[ConversationId],
             CASE WHEN m.[Kind] = 'Attachment' AND m.[AudioDurationMs] IS NOT NULL THEN 1 ELSE 0 END AS IsVoiceNote
      FROM [dbo].[AttachmentTracking] a
      JOIN [dbo].[Message] m ON m.[Id] = a.[MessageId] AND m.[TenantId] = a.[TenantId]
      WHERE a.[TenantId] = ${input.tenantId} AND a.[FileId] IN (${Prisma.join([...input.fileIds])})
        AND ${NOT_A_TRANSCRIPT}`;
    return rows.map((r) => ({
      fileId: r.FileId.toLowerCase(),
      conversationId: r.ConversationId.toLowerCase(),
      isVoiceNote: Number(r.IsVoiceNote) === 1,
    }));
  }

  async listConversationClients(input: {
    tenantId: string;
    conversationIds: readonly string[];
  }): Promise<ConversationClientRow[]> {
    if (input.conversationIds.length === 0) return [];
    const rows = await this.prisma.$queryRaw<
      { ConversationId: string; ActorType: string; CustomerId: string | null; IsRemoved: boolean | number }[]
    >`
      SELECT p.[ConversationId], p.[ActorType], p.[CustomerId], p.[IsRemoved]
      FROM [dbo].[ConversationParticipant] p
      WHERE p.[TenantId] = ${input.tenantId} AND p.[ConversationId] IN (${Prisma.join([...input.conversationIds])})
        AND (p.[ActorType] = 'CustomerPortal' OR p.[CustomerId] IS NOT NULL)`;
    return rows.map((r) => ({
      conversationId: r.ConversationId.toLowerCase(),
      actorType: r.ActorType,
      customerId: r.CustomerId?.toLowerCase() ?? null,
      isRemoved: Boolean(r.IsRemoved),
    }));
  }
  async listCallRecordingScope(input: {
    tenantId: string;
    userId: string;
    clientCalls: 'none' | 'all' | 'assigned';
    take: number;
  }): Promise<RecordingScopeRow[]> {
    // E-1: participó; con call.record, también las que tienen un cliente (asignado, si se pide).
    const assigned =
      input.clientCalls === 'assigned'
        ? Prisma.sql`AND EXISTS (
            SELECT 1 FROM [dbo].[CustomerAssignmentProjection] cap
            WHERE cap.[TenantId] = c.[TenantId] AND cap.[UserId] = ${input.userId} AND cap.[CustomerId] = cp.[CustomerId])`
        : Prisma.empty;
    const withClient =
      input.clientCalls === 'none'
        ? Prisma.empty
        : Prisma.sql`OR EXISTS (
            SELECT 1 FROM [dbo].[CallParticipant] cp
            WHERE cp.[CallId] = c.[Id] AND cp.[CustomerId] IS NOT NULL ${assigned})`;
    const rows = await this.prisma.$queryRaw<RecordingScopeRaw[]>`
      SELECT TOP (${input.take}) c.[Id] AS ScopeId, ${READY_AT} AS LastActivityAtUtc
      FROM [dbo].[RecordingSession] s
      JOIN [dbo].[Call] c ON c.[Id] = s.[ScopeId] AND c.[TenantId] = s.[TenantId]
      WHERE s.[TenantId] = ${input.tenantId} AND s.[Scope] = 'Call' AND s.[State] = 'Ready' AND s.[PurgedAtUtc] IS NULL
        AND (c.[CallerUserId] = ${input.userId} OR c.[CalleeUserId] = ${input.userId} ${withClient})
      GROUP BY c.[Id]
      ORDER BY ${READY_AT} DESC, c.[Id]`;
    return rows.map(toRecordingScopeRow);
  }

  async listMeetingRecordingScope(input: {
    tenantId: string;
    userId: string;
    take: number;
  }): Promise<RecordingScopeRow[]> {
    const rows = await this.prisma.$queryRaw<RecordingScopeRaw[]>`
      SELECT TOP (${input.take}) m.[Id] AS ScopeId, ${READY_AT} AS LastActivityAtUtc
      FROM [dbo].[RecordingSession] s
      JOIN [dbo].[Meeting] m ON m.[Id] = s.[ScopeId] AND m.[TenantId] = s.[TenantId]
      WHERE s.[TenantId] = ${input.tenantId} AND s.[Scope] = 'Meeting' AND s.[State] = 'Ready' AND s.[PurgedAtUtc] IS NULL
        AND (m.[HostUserId] = ${input.userId}
          OR EXISTS (SELECT 1 FROM [dbo].[MeetingParticipant] mp WHERE mp.[MeetingId] = m.[Id] AND mp.[UserId] = ${input.userId}))
      GROUP BY m.[Id]
      ORDER BY ${READY_AT} DESC, m.[Id]`;
    return rows.map(toRecordingScopeRow);
  }

  async findRecordingFiles(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RecordingFileRow[]> {
    if (input.fileIds.length === 0) return [];
    const ids = Prisma.join([...input.fileIds]);
    // Grabaciones listas no purgadas y .txt listos de una sesión no purgada; nunca pistas ni .json.
    const rows = await this.prisma.$queryRaw<
      { FileId: string; Scope: string; ScopeId: string; Role: string; Sequence: number; ReadyCount: number }[]
    >`
      SELECT f.[FileId], f.[Scope], f.[ScopeId], f.[Role], f.[Sequence],
             (SELECT COUNT(*) FROM [dbo].[RecordingSession] x
              WHERE x.[TenantId] = ${input.tenantId} AND x.[Scope] = f.[Scope] AND x.[ScopeId] = f.[ScopeId]
                AND x.[State] = 'Ready' AND x.[PurgedAtUtc] IS NULL) AS ReadyCount
      FROM (
        SELECT s.[RecordingFileId] AS FileId, s.[Scope], s.[ScopeId], 'recording' AS Role, s.[Sequence]
        FROM [dbo].[RecordingSession] s
        WHERE s.[TenantId] = ${input.tenantId} AND s.[State] = 'Ready' AND s.[PurgedAtUtc] IS NULL
          AND s.[RecordingFileId] IN (${ids})
        UNION ALL
        SELECT t.[TxtFileId], t.[Scope], t.[ScopeId], 'transcript', s.[Sequence]
        FROM [dbo].[TranscriptArtifact] t
        JOIN [dbo].[RecordingSession] s ON ${TRANSCRIPT_SESSION}
        WHERE t.[TenantId] = ${input.tenantId} AND t.[State] = 'Ready' AND t.[Scope] IN ('Call', 'Meeting')
          AND s.[PurgedAtUtc] IS NULL AND t.[TxtFileId] IN (${ids})
      ) f`;
    return rows.map((r) => ({
      fileId: r.FileId.toLowerCase(),
      scope: r.Scope as RecordingKind,
      scopeId: r.ScopeId.toLowerCase(),
      role: r.Role === 'recording' ? 'recording' : 'transcript',
      sequence: Number(r.Sequence),
      readyCount: Number(r.ReadyCount),
    }));
  }

  async listRecordingsForRedeclare(input: {
    tenantId: string;
    afterId: string | null;
    take: number;
  }): Promise<RedeclareRecordingRow[]> {
    const after = input.afterId
      ? Prisma.sql`AND s.[Id] > CAST(${input.afterId} AS uniqueidentifier)`
      : Prisma.empty;
    const rows = await this.prisma.$queryRaw<
      { Id: string; Scope: string; ScopeId: string; FileId: string }[]
    >`
      SELECT TOP (${input.take}) s.[Id], s.[Scope], s.[ScopeId], s.[RecordingFileId] AS FileId
      FROM [dbo].[RecordingSession] s
      WHERE s.[TenantId] = ${input.tenantId} AND s.[RecordingFileId] IS NOT NULL AND s.[PurgedAtUtc] IS NULL ${after}
      ORDER BY s.[Id]`;
    return rows.map((r) => ({
      id: r.Id.toLowerCase(),
      scope: r.Scope as RecordingKind,
      scopeId: r.ScopeId.toLowerCase(),
      fileId: r.FileId.toLowerCase(),
    }));
  }

  async listTranscriptsForRedeclare(input: {
    tenantId: string;
    afterId: string | null;
    take: number;
  }): Promise<RedeclareTranscriptRow[]> {
    const after = input.afterId
      ? Prisma.sql`AND t.[Id] > CAST(${input.afterId} AS uniqueidentifier)`
      : Prisma.empty;
    const rows = await this.prisma.$queryRaw<
      { Id: string; Scope: string; ScopeId: string; TxtFileId: string | null; JsonFileId: string | null }[]
    >`
      SELECT TOP (${input.take}) t.[Id], t.[Scope], t.[ScopeId], t.[TxtFileId], t.[JsonFileId]
      FROM [dbo].[TranscriptArtifact] t
      WHERE t.[TenantId] = ${input.tenantId} AND t.[State] = 'Ready' AND t.[Scope] IN ('Call', 'Meeting') ${after}
        AND EXISTS (SELECT 1 FROM [dbo].[RecordingSession] s WHERE ${TRANSCRIPT_SESSION} AND s.[PurgedAtUtc] IS NULL)
      ORDER BY t.[Id]`;
    return rows.map((r) => ({
      id: r.Id.toLowerCase(),
      scope: r.Scope as RecordingKind,
      scopeId: r.ScopeId.toLowerCase(),
      txtFileId: r.TxtFileId?.toLowerCase() ?? null,
      jsonFileId: r.JsonFileId?.toLowerCase() ?? null,
    }));
  }

  async findRecordingsForRedeclare(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RedeclareRecordingRow[]> {
    if (input.fileIds.length === 0) return [];
    const rows = await this.prisma.$queryRaw<
      { Id: string; Scope: string; ScopeId: string; FileId: string }[]
    >`
      SELECT s.[Id], s.[Scope], s.[ScopeId], s.[RecordingFileId] AS FileId
      FROM [dbo].[RecordingSession] s
      WHERE s.[TenantId] = ${input.tenantId} AND s.[PurgedAtUtc] IS NULL
        AND s.[RecordingFileId] IN (${Prisma.join([...input.fileIds])})`;
    return rows.map((r) => ({
      id: r.Id.toLowerCase(),
      scope: r.Scope as RecordingKind,
      scopeId: r.ScopeId.toLowerCase(),
      fileId: r.FileId.toLowerCase(),
    }));
  }

  async findTranscriptsForRedeclare(input: {
    tenantId: string;
    fileIds: readonly string[];
  }): Promise<RedeclareTranscriptRow[]> {
    if (input.fileIds.length === 0) return [];
    const ids = Prisma.join([...input.fileIds]);
    const rows = await this.prisma.$queryRaw<
      { Id: string; Scope: string; ScopeId: string; TxtFileId: string | null; JsonFileId: string | null }[]
    >`
      SELECT t.[Id], t.[Scope], t.[ScopeId], t.[TxtFileId], t.[JsonFileId]
      FROM [dbo].[TranscriptArtifact] t
      WHERE t.[TenantId] = ${input.tenantId} AND t.[State] = 'Ready' AND t.[Scope] IN ('Call', 'Meeting')
        AND (t.[TxtFileId] IN (${ids}) OR t.[JsonFileId] IN (${ids}))
        AND EXISTS (SELECT 1 FROM [dbo].[RecordingSession] s WHERE ${TRANSCRIPT_SESSION} AND s.[PurgedAtUtc] IS NULL)`;
    return rows.map((r) => ({
      id: r.Id.toLowerCase(),
      scope: r.Scope as RecordingKind,
      scopeId: r.ScopeId.toLowerCase(),
      txtFileId: r.TxtFileId?.toLowerCase() ?? null,
      jsonFileId: r.JsonFileId?.toLowerCase() ?? null,
    }));
  }

  async listRecordingClients(input: {
    tenantId: string;
    scope: RecordingKind;
    scopeIds: readonly string[];
  }): Promise<RecordingClientRow[]> {
    if (input.scopeIds.length === 0) return [];
    const ids = Prisma.join([...input.scopeIds]);
    const rows =
      input.scope === 'Call'
        ? await this.prisma.$queryRaw<{ ScopeId: string; ActorType: string; CustomerId: string | null }[]>`
            SELECT p.[CallId] AS ScopeId, 'CustomerPortal' AS ActorType, p.[CustomerId]
            FROM [dbo].[CallParticipant] p
            WHERE p.[TenantId] = ${input.tenantId} AND p.[CallId] IN (${ids}) AND p.[CustomerId] IS NOT NULL`
        : await this.prisma.$queryRaw<{ ScopeId: string; ActorType: string; CustomerId: string | null }[]>`
            SELECT p.[MeetingId] AS ScopeId, p.[ActorType], p.[CustomerId]
            FROM [dbo].[MeetingParticipant] p
            WHERE p.[TenantId] = ${input.tenantId} AND p.[MeetingId] IN (${ids})
              AND (p.[ActorType] = 'CustomerPortal' OR p.[CustomerId] IS NOT NULL)`;
    return rows.map((r) => ({
      scopeId: r.ScopeId.toLowerCase(),
      actorType: r.ActorType,
      customerId: r.CustomerId?.toLowerCase() ?? null,
    }));
  }
}

function toScopeRow(row: ScopeRaw): ChatScopeRow {
  return { conversationId: row.ConversationId.toLowerCase(), lastActivityAtUtc: row.LastActivityAtUtc };
}

function toRecordingScopeRow(row: RecordingScopeRaw): RecordingScopeRow {
  return { scopeId: row.ScopeId.toLowerCase(), lastActivityAtUtc: row.LastActivityAtUtc };
}
