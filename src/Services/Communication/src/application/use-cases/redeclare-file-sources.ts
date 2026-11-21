import { FileSourceResourceTypes, FileSourceRoles } from '../../contracts/events/cloudstorage-events.js';
import type {
  FileLibraryReadRepository,
  RecordingKind,
  RedeclareChatRow,
  RedeclareRecordingRow,
  RedeclareTranscriptRow,
} from '../ports/file-library-read-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import {
  chatFileRole,
  declareFileSource,
  recordingResourceType,
  relatedCustomerOf,
  type ClientCandidate,
} from '../services/file-source-declarer.js';

/** Orígenes que se pueden volver a declarar. */
export const RedeclareKinds = ['chat', 'recordings'] as const;
export type RedeclareKind = (typeof RedeclareKinds)[number];

/** Tandas del recorrido: adjuntos de chat y grabaciones o transcripts. */
export const REDECLARE_CHAT_BATCH = 500;
export const REDECLARE_RECORDINGS_BATCH = 200;

export interface RedeclareFileSourcesCommand {
  readonly tenantId: string;
  readonly kind: RedeclareKind;
  readonly dryRun: boolean;
  readonly correlationId: string | undefined;
}

export interface RedeclareFileSourcesResult {
  readonly dryRun: boolean;
  readonly scanned: number;
  readonly declared: number;
}

export interface RedeclareFileSourcesDeps {
  readonly fileLibrary: Pick<
    FileLibraryReadRepository,
    | 'listChatAttachmentsForRedeclare'
    | 'listConversationClients'
    | 'listRecordingsForRedeclare'
    | 'listTranscriptsForRedeclare'
    | 'listRecordingClients'
  >;
  readonly publisher: Pick<IntegrationEventPublisher, 'enqueue'>;
}

/** Un archivo de grabación a declarar. */
export interface RecordingDeclaration {
  readonly fileId: string;
  readonly scope: RecordingKind;
  readonly scopeId: string;
  readonly role: string;
}

/**
 * Backfill de la biblioteca: vuelve a publicar la declaración de los archivos existentes de una oficina. Solo publica
 * eventos (uno por archivo, por el outbox); no escribe en la base de Communication. Repetirlo es inofensivo.
 */
export async function redeclareFileSources(
  command: RedeclareFileSourcesCommand,
  deps: RedeclareFileSourcesDeps,
): Promise<RedeclareFileSourcesResult> {
  const counts =
    command.kind === 'chat' ? await redeclareChat(command, deps) : await redeclareRecordings(command, deps);
  return { dryRun: command.dryRun, ...counts };
}

type Counts = { scanned: number; declared: number };

/** Recorre por claves en tandas hasta la última corta. */
async function walk<T>(
  fetch: (after: string | null) => Promise<readonly T[]>,
  keyOf: (row: T) => string,
  batch: number,
  onPage: (page: readonly T[]) => Promise<void>,
): Promise<void> {
  let after: string | null = null;
  for (;;) {
    const page = await fetch(after);
    if (page.length === 0) return;
    await onPage(page);
    const last = page.at(-1);
    after = last ? keyOf(last) : null;
    if (page.length < batch) return;
  }
}

async function redeclareChat(
  command: RedeclareFileSourcesCommand,
  deps: RedeclareFileSourcesDeps,
): Promise<Counts> {
  const counts = { scanned: 0, declared: 0 };
  await walk(
    (after) =>
      deps.fileLibrary.listChatAttachmentsForRedeclare({
        tenantId: command.tenantId,
        afterFileId: after,
        take: REDECLARE_CHAT_BATCH,
      }),
    (row) => row.fileId,
    REDECLARE_CHAT_BATCH,
    async (page) => {
      counts.scanned += page.length;
      if (!command.dryRun) counts.declared += await declareChatPage(command, page, deps);
    },
  );
  return counts;
}

/** Tenant y correlación con que se declara. */
export type DeclareContext = Pick<RedeclareFileSourcesCommand, 'tenantId' | 'correlationId'>;

/** Declara adjuntos de chat con el cliente de su conversación (lo reutiliza el reclamo de CloudStorage). */
export async function declareChatPage(
  command: DeclareContext,
  page: readonly RedeclareChatRow[],
  deps: {
    readonly fileLibrary: Pick<FileLibraryReadRepository, 'listConversationClients'>;
    readonly publisher: Pick<IntegrationEventPublisher, 'enqueue'>;
  },
): Promise<number> {
  const conversationIds = [...new Set(page.map((row) => row.conversationId))];
  const clients = await deps.fileLibrary.listConversationClients({
    tenantId: command.tenantId,
    conversationIds,
  });
  const byConversation = groupBy(clients, (c) => c.conversationId);
  for (const row of page) {
    await declareFileSource(deps.publisher, {
      tenantId: command.tenantId,
      correlationId: command.correlationId,
      fileId: row.fileId,
      resourceType: FileSourceResourceTypes.Conversation,
      resourceId: row.conversationId,
      role: chatFileRole(row.isVoiceNote),
      relatedCustomerId: relatedCustomerOf(byConversation.get(row.conversationId) ?? []),
    });
  }
  return page.length;
}

// Grabaciones con archivo y sin purgar; transcripts Ready de sesiones no purgadas (txt → transcript, json →
// transcript_data). Las purgadas se saltan: su archivo ya no existe en CloudStorage.
async function redeclareRecordings(
  command: RedeclareFileSourcesCommand,
  deps: RedeclareFileSourcesDeps,
): Promise<Counts> {
  const counts = { scanned: 0, declared: 0 };
  const onFiles = async (files: readonly RecordingDeclaration[]) => {
    counts.scanned += files.length;
    if (!command.dryRun) counts.declared += await declareRecordingFiles(command, files, deps);
  };
  const page = { tenantId: command.tenantId, take: REDECLARE_RECORDINGS_BATCH };
  await walk(
    (after) => deps.fileLibrary.listRecordingsForRedeclare({ ...page, afterId: after }),
    (row) => row.id,
    REDECLARE_RECORDINGS_BATCH,
    (rows) => onFiles(rows.map(recordingDeclaration)),
  );
  await walk(
    (after) => deps.fileLibrary.listTranscriptsForRedeclare({ ...page, afterId: after }),
    (row) => row.id,
    REDECLARE_RECORDINGS_BATCH,
    (rows) => onFiles(rows.flatMap(transcriptDeclarations)),
  );
  return counts;
}

/** Archivo de una grabación con su papel. */
export function recordingDeclaration(r: RedeclareRecordingRow): RecordingDeclaration {
  return { fileId: r.fileId, scope: r.scope, scopeId: r.scopeId, role: FileSourceRoles.Recording };
}

/** Archivos de un transcript: `.txt` → transcript, `.json` → transcript_data. */
export function transcriptDeclarations(r: RedeclareTranscriptRow): RecordingDeclaration[] {
  return [
    ...(r.txtFileId
      ? [{ fileId: r.txtFileId, scope: r.scope, scopeId: r.scopeId, role: FileSourceRoles.Transcript }]
      : []),
    ...(r.jsonFileId
      ? [{ fileId: r.jsonFileId, scope: r.scope, scopeId: r.scopeId, role: FileSourceRoles.TranscriptData }]
      : []),
  ];
}

/** Declara grabaciones y transcripts con el cliente de su llamada o reunión. */
export async function declareRecordingFiles(
  command: DeclareContext,
  files: readonly RecordingDeclaration[],
  deps: {
    readonly fileLibrary: Pick<FileLibraryReadRepository, 'listRecordingClients'>;
    readonly publisher: Pick<IntegrationEventPublisher, 'enqueue'>;
  },
): Promise<number> {
  const clients = new Map<string, ClientCandidate[]>();
  for (const scope of ['Call', 'Meeting'] as const) {
    const scopeIds = [...new Set(files.filter((f) => f.scope === scope).map((f) => f.scopeId))];
    if (scopeIds.length === 0) continue;
    const rows = await deps.fileLibrary.listRecordingClients({ tenantId: command.tenantId, scope, scopeIds });
    for (const [scopeId, group] of groupBy(rows, (r) => r.scopeId)) {
      clients.set(
        `${scope}:${scopeId}`,
        group.map((r) => ({ ...r, isRemoved: false })),
      );
    }
  }
  for (const file of files) {
    await declareFileSource(deps.publisher, {
      tenantId: command.tenantId,
      correlationId: command.correlationId,
      fileId: file.fileId,
      resourceType: recordingResourceType(file.scope),
      resourceId: file.scopeId,
      role: file.role,
      relatedCustomerId: relatedCustomerOf(clients.get(`${file.scope}:${file.scopeId}`) ?? []),
    });
  }
  return files.length;
}

function groupBy<T>(rows: readonly T[], keyOf: (row: T) => string): Map<string, T[]> {
  const groups = new Map<string, T[]>();
  for (const row of rows) groups.set(keyOf(row), [...(groups.get(keyOf(row)) ?? []), row]);
  return groups;
}
