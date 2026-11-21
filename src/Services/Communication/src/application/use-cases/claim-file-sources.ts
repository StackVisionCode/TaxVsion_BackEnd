import { FILE_SOURCES_CLAIM_MAX_IDS } from '../../contracts/events/cloudstorage-events.js';
import type { FileLibraryReadRepository } from '../ports/file-library-read-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import {
  declareChatPage,
  declareRecordingFiles,
  recordingDeclaration,
  transcriptDeclarations,
} from './redeclare-file-sources.js';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export interface ClaimFileSourcesCommand {
  readonly tenantId: string;
  readonly fileIds: readonly unknown[];
  readonly correlationId: string | undefined;
}

export interface ClaimFileSourcesDeps {
  readonly fileLibrary: Pick<
    FileLibraryReadRepository,
    | 'findChatAttachmentsForRedeclare'
    | 'listConversationClients'
    | 'findRecordingsForRedeclare'
    | 'findTranscriptsForRedeclare'
    | 'listRecordingClients'
  >;
  readonly publisher: Pick<IntegrationEventPublisher, 'enqueue'>;
}

/**
 * CloudStorage pregunta de quién son archivos sin origen: se declaran solo los de Communication en ese tenant,
 * con los mismos valores que la reconciliación manual. Los ids ajenos se ignoran. Devuelve cuántos declaró.
 */
export async function claimFileSources(
  command: ClaimFileSourcesCommand,
  deps: ClaimFileSourcesDeps,
): Promise<number> {
  const fileIds = normalizeIds(command.fileIds);
  if (fileIds.length === 0) return 0;
  const input = { tenantId: command.tenantId, fileIds };
  const requested = new Set(fileIds);

  const chat = await deps.fileLibrary.findChatAttachmentsForRedeclare(input);
  const recordings = (await deps.fileLibrary.findRecordingsForRedeclare(input)).map(recordingDeclaration);
  // De un transcript solo los archivos pedidos (el .txt y el .json pueden llegar en pedidos distintos).
  const transcripts = (await deps.fileLibrary.findTranscriptsForRedeclare(input))
    .flatMap(transcriptDeclarations)
    .filter((f) => requested.has(f.fileId));

  let declared = 0;
  if (chat.length > 0) declared += await declareChatPage(command, chat, deps);
  const files = [...recordings, ...transcripts];
  if (files.length > 0) declared += await declareRecordingFiles(command, files, deps);
  return declared;
}

/** GUID válidos, en minúsculas, sin repetir y con el tope del contrato. */
function normalizeIds(raw: readonly unknown[]): string[] {
  const ids = raw
    .filter((v): v is string => typeof v === 'string' && GUID.test(v))
    .map((v) => v.toLowerCase());
  return [...new Set(ids)].slice(0, FILE_SOURCES_CLAIM_MAX_IDS);
}
