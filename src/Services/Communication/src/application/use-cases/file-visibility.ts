import type { Conversation } from '../../domain/conversations/conversation.js';
import { ConversationKind } from '../../domain/conversations/conversation-kind.js';
import {
  FileVisibilityResourceTypes,
  FileVisibilityRestrictions,
  type VisibleFileItem,
} from '../../contracts/http/files-http.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { FileLibraryReadRepository, RecordingFileRow } from '../ports/file-library-read-repository.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
import { relatedCustomerOf } from '../services/file-source-declarer.js';
import {
  callCustomerIds,
  canSeeRecording,
  participatedInRecording,
  recordingClientCandidates,
  type RecordingSubject,
} from '../services/recording-visibility.js';
import {
  canOfficeReadClosedClientChat,
  type ClosedClientChatAccessDeps,
} from './closed-client-chat-access.js';
import type { FileLibraryViewer } from './file-scope.js';

export interface FileVisibilityDeps extends ClosedClientChatAccessDeps {
  readonly fileLibrary: Pick<FileLibraryReadRepository, 'findChatAttachments' | 'findRecordingFiles'>;
  readonly conversations: Pick<ConversationRepository, 'findById'>;
  readonly supportTickets?: Pick<SupportTicketRepository, 'findById' | 'findByConversationId'> | undefined;
  // Rama de grabaciones: sin estos datos no responde.
  readonly meetings?: Pick<MeetingRepository, 'findById'> | undefined;
  readonly calls?: Pick<CallRepository, 'findById'> | undefined;
  readonly customerAssignments?: Pick<CustomerAssignmentProjectionRepository, 'isAssigned'> | undefined;
  readonly assignmentVisibilityEnabled?: boolean | undefined;
}

/**
 * `POST /communication/files/visibility`: de los archivos pedidos, los que quien mira abre hoy en su módulo. Lo demás
 * se omite. Un archivo de grabación o de transcript se contesta solo por la rama de grabaciones (TRW-1).
 */
export async function getVisibleFiles(
  viewer: FileLibraryViewer,
  fileIds: readonly string[],
  deps: FileVisibilityDeps,
): Promise<VisibleFileItem[]> {
  if (fileIds.length === 0) return [];
  const recordings = await visibleRecordings(viewer, fileIds, deps);
  const chat = await visibleChatFiles(
    viewer,
    fileIds.filter((id) => !recordings.claimed.has(id)),
    deps,
  );
  return [...recordings.items, ...chat];
}

/**
 * Rama de chat: exige tracking `Available` de un mensaje vivo (lo filtra la lectura) y membresía activa, o la lectura
 * de un chat de cliente cerrado. Sin lectura de admin: quien no está en el chat no recibe nada.
 */
async function visibleChatFiles(
  viewer: FileLibraryViewer,
  fileIds: readonly string[],
  deps: FileVisibilityDeps,
): Promise<VisibleFileItem[]> {
  if (!viewer.canReadChat || fileIds.length === 0) return [];
  const attachments = await deps.fileLibrary.findChatAttachments({ tenantId: viewer.tenantId, fileIds });
  const byConversation = new Map<string, string[]>();
  for (const a of attachments)
    byConversation.set(a.conversationId, [...(byConversation.get(a.conversationId) ?? []), a.fileId]);

  const groups = await Promise.all(
    [...byConversation.entries()].map(async ([conversationId, files]) => {
      const conversation = await deps.conversations.findById(viewer.tenantId, conversationId, 0);
      if (!conversation) return [];
      const restrictedTo = await accessOf(conversation, viewer, deps);
      if (restrictedTo === undefined) return [];
      const resource = resourceOf(conversation);
      const title = await titleOf(conversation, viewer, deps);
      const customerId = relatedCustomerOf(conversation.getParticipantSnapshots());
      return files.map(
        (fileId): VisibleFileItem => ({
          fileId,
          ...resource,
          title,
          customerId,
          context: null,
          canDownload: true,
          restrictedTo,
        }),
      );
    }),
  );
  return groups.flat();
}

// undefined = no lo ve; "participants" por membresía; null por la lectura de un chat de cliente cerrado.
async function accessOf(
  conversation: Conversation,
  viewer: FileLibraryViewer,
  deps: ClosedClientChatAccessDeps,
): Promise<string | null | undefined> {
  if (conversation.isParticipant(viewer.userId)) return FileVisibilityRestrictions.Participants;
  const closed = await canOfficeReadClosedClientChat(
    conversation,
    { userId: viewer.userId, actorType: viewer.actorType },
    deps,
  );
  return closed ? null : undefined;
}

function resourceOf(conversation: Conversation): { resourceType: string; resourceId: string } {
  if (conversation.kind === ConversationKind.Support && conversation.supportTicketId) {
    return {
      resourceType: FileVisibilityResourceTypes.SupportTicket,
      resourceId: conversation.supportTicketId,
    };
  }
  if (conversation.kind === ConversationKind.Meeting && conversation.meetingId) {
    return { resourceType: FileVisibilityResourceTypes.Meeting, resourceId: conversation.meetingId };
  }
  return { resourceType: FileVisibilityResourceTypes.Conversation, resourceId: conversation.id };
}

/** Nombre del grupo, título de la reunión, asunto del ticket o el otro participante del Direct. */
async function titleOf(
  conversation: Conversation,
  viewer: FileLibraryViewer,
  deps: FileVisibilityDeps,
): Promise<string | null> {
  if (conversation.kind === ConversationKind.Support) {
    const ticketId = conversation.supportTicketId;
    // Tickets viejos: la conversación guarda un id que no es el del ticket.
    const ticket =
      (ticketId ? await deps.supportTickets?.findById(ticketId) : null) ??
      (await deps.supportTickets?.findByConversationId?.(conversation.id)) ??
      null;
    return ticket?.toSnapshot().subject ?? null;
  }
  if (conversation.kind !== ConversationKind.Direct) return conversation.title;
  // Quien lee un chat cerrado sin ser parte ve el nombre del cliente.
  const other =
    conversation.closedChatClient() && !conversation.isParticipant(viewer.userId)
      ? conversation.closedChatClient()
      : conversation.getParticipantSnapshots().find((p) => p.userId !== viewer.userId);
  return other?.displayName ?? null;
}

/**
 * Rama de grabaciones (E-1): grabación `Ready` no purgada o `.txt` `Ready` de una sesión no purgada (lo filtra la
 * lectura), con el permiso de entrada de su tipo y `canSeeRecording`. `claimed` = archivos que son de grabaciones,
 * los vea o no quien mira.
 */
async function visibleRecordings(
  viewer: FileLibraryViewer,
  fileIds: readonly string[],
  deps: FileVisibilityDeps,
): Promise<{ items: VisibleFileItem[]; claimed: ReadonlySet<string> }> {
  const { meetings, calls, userPermissions, customerAssignments } = deps;
  if (
    (!viewer.canReadCalls && !viewer.canReadMeetings) ||
    !meetings ||
    !calls ||
    !userPermissions ||
    !customerAssignments
  ) {
    return { items: [], claimed: new Set() };
  }
  const rows = await deps.fileLibrary.findRecordingFiles({ tenantId: viewer.tenantId, fileIds });
  const byScope = new Map<string, RecordingFileRow[]>();
  for (const row of rows) {
    const key = `${row.scope}:${row.scopeId}`;
    byScope.set(key, [...(byScope.get(key) ?? []), row]);
  }
  const clientAccess = {
    userPermissions,
    customerAssignments,
    assignmentVisibilityEnabled: deps.assignmentVisibilityEnabled ?? false,
  };
  const recordViewer = { ...viewer, holdsCallRecordPermission: viewer.holdsCallRecordPermission ?? false };
  const groups = await Promise.all(
    [...byScope.values()].map(async (group) => {
      const first = group[0];
      if (!first || !(first.scope === 'Call' ? viewer.canReadCalls : viewer.canReadMeetings)) return [];
      const subject = await subjectOf(first, viewer.tenantId, meetings, calls);
      if (!subject || !(await canSeeRecording(recordViewer, subject, clientAccess))) return [];
      return group.map((row) => recordingItem(row, subject, viewer));
    }),
  );
  return { items: groups.flat(), claimed: new Set(rows.map((r) => r.fileId)) };
}

async function subjectOf(
  row: RecordingFileRow,
  tenantId: string,
  meetings: Pick<MeetingRepository, 'findById'>,
  calls: Pick<CallRepository, 'findById'>,
): Promise<RecordingSubject | null> {
  if (row.scope === 'Call') {
    const call = await calls.findById(tenantId, row.scopeId);
    return call ? { scope: 'Call', call } : null;
  }
  const meeting = await meetings.findById(tenantId, row.scopeId);
  return meeting ? { scope: 'Meeting', meeting } : null;
}

function recordingItem(
  row: RecordingFileRow,
  subject: RecordingSubject,
  viewer: FileLibraryViewer,
): VisibleFileItem {
  const participated = participatedInRecording(subject, viewer.userId);
  // Solo la ven quienes participaron, salvo una llamada con cliente (también quien graba y ve a ese cliente).
  const sharedBeyondParticipants = subject.scope === 'Call' && callCustomerIds(subject.call).length > 0;
  const base = recordingTitle(subject, viewer.userId, participated);
  return {
    fileId: row.fileId,
    resourceType:
      subject.scope === 'Call' ? FileVisibilityResourceTypes.Call : FileVisibilityResourceTypes.Meeting,
    resourceId: row.scopeId,
    title: base && row.readyCount > 1 ? `${base} · Recording ${row.sequence}` : base,
    customerId: relatedCustomerOf(recordingClientCandidates(subject)),
    context: { participant: participated ? 'true' : 'false' },
    canDownload: true,
    restrictedTo: sharedBeyondParticipants ? null : FileVisibilityRestrictions.Participants,
  };
}

/** Título de la reunión, o "Call with" el otro participante (el cliente si quien mira no estuvo). */
function recordingTitle(subject: RecordingSubject, userId: string, participated: boolean): string | null {
  if (subject.scope === 'Meeting') return subject.meeting.toSnapshot().title;
  const participants = subject.call.toSnapshot().participants ?? [];
  const other = participated
    ? participants.find((p) => p.userId !== userId)
    : (participants.find((p) => !!p.customerId) ?? participants[0]);
  return other ? `Call with ${other.displayName}` : 'Call';
}
