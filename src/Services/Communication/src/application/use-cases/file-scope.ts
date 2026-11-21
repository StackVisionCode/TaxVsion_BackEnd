import { isStaffActor } from '../../domain/shared/permissions.js';
import {
  FileScopeLists,
  FilesLimits,
  type CommunicationFileScopeResponse,
} from '../../contracts/http/files-http.js';
import type { FileLibraryReadRepository } from '../ports/file-library-read-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import { seesAllCustomers } from './customer-visibility.js';

/** Quien mira la biblioteca, con lo que ya resolvió la ruta. */
export interface FileLibraryViewer {
  readonly tenantId: string;
  readonly userId: string;
  readonly actorType: string;
  /** `communication.chat.reply` con el módulo `comms`. */
  readonly canReadChat: boolean;
  /** `communication.call.start` con el módulo `comms`. */
  readonly canReadCalls?: boolean | undefined;
  /** `communication.meeting.join` con el módulo `meetings`. */
  readonly canReadMeetings?: boolean | undefined;
  /** `communication.call.record`: amplía a las llamadas con cliente donde no participó. */
  readonly holdsCallRecordPermission?: boolean | undefined;
}

export interface FileScopeDeps {
  readonly fileLibrary: Pick<
    FileLibraryReadRepository,
    | 'listMemberChatScope'
    | 'listClosedClientChatScope'
    | 'listCallRecordingScope'
    | 'listMeetingRecordingScope'
  >;
  readonly userPermissions?: UserPermissionsProjectionRepository | undefined;
  /** Visibilidad por asignación activa en el despliegue. */
  readonly assignmentVisibilityEnabled?: boolean | undefined;
}

interface ScopeCandidate {
  readonly id: string;
  readonly lastActivityAtUtc: Date | null;
}

interface ScopeList {
  readonly ids: string[];
  readonly truncated: boolean;
}

const EMPTY: ScopeList = { ids: [], truncated: false };
const TAKE = FilesLimits.ScopeMaxIds + 1;

/**
 * Ámbito de la biblioteca (`GET /communication/files/scope`): conversaciones, llamadas y reuniones cuyos archivos puede
 * ver quien mira, las de actividad más reciente primero. Es una optimización: cada archivo lo sigue confirmando la
 * visibilidad.
 */
export async function getFileScope(
  viewer: FileLibraryViewer,
  deps: FileScopeDeps,
): Promise<CommunicationFileScopeResponse> {
  const [conversations, calls, meetings] = await Promise.all([
    viewer.canReadChat ? chatScope(viewer, deps) : EMPTY,
    viewer.canReadCalls ? callScope(viewer, deps) : EMPTY,
    viewer.canReadMeetings ? meetingScope(viewer, deps) : EMPTY,
  ]);
  return {
    conversationIds: conversations.ids,
    callIds: calls.ids,
    meetingIds: meetings.ids,
    truncated: [
      ...(conversations.truncated ? [FileScopeLists.Conversations] : []),
      ...(calls.truncated ? [FileScopeLists.Calls] : []),
      ...(meetings.truncated ? [FileScopeLists.Meetings] : []),
    ],
  };
}

// Membresía activa más los chats de cliente cerrados que la oficina lee (misma regla que closed-client-chat-access).
async function chatScope(viewer: FileLibraryViewer, deps: FileScopeDeps): Promise<ScopeList> {
  const member = await deps.fileLibrary.listMemberChatScope({
    tenantId: viewer.tenantId,
    userId: viewer.userId,
    take: TAKE,
  });
  const closedAccess = await closedChatAccess(viewer, deps);
  const closed =
    closedAccess === 'none'
      ? []
      : await deps.fileLibrary.listClosedClientChatScope({
          tenantId: viewer.tenantId,
          userId: viewer.userId,
          assignedOnly: closedAccess === 'assigned',
          take: TAKE,
        });
  return capped(
    [...member, ...closed].map((r) => ({ id: r.conversationId, lastActivityAtUtc: r.lastActivityAtUtc })),
  );
}

// E-1: las llamadas donde participó; con call.record, además las que tienen un cliente que ve.
async function callScope(viewer: FileLibraryViewer, deps: FileScopeDeps): Promise<ScopeList> {
  const rows = await deps.fileLibrary.listCallRecordingScope({
    tenantId: viewer.tenantId,
    userId: viewer.userId,
    clientCalls: await clientCallsAccess(viewer, deps),
    take: TAKE,
  });
  return capped(rows.map((r) => ({ id: r.scopeId, lastActivityAtUtc: r.lastActivityAtUtc })));
}

// E-1: solo reuniones donde fue anfitrión o participante.
async function meetingScope(viewer: FileLibraryViewer, deps: FileScopeDeps): Promise<ScopeList> {
  const rows = await deps.fileLibrary.listMeetingRecordingScope({
    tenantId: viewer.tenantId,
    userId: viewer.userId,
    take: TAKE,
  });
  return capped(rows.map((r) => ({ id: r.scopeId, lastActivityAtUtc: r.lastActivityAtUtc })));
}

async function closedChatAccess(
  viewer: FileLibraryViewer,
  deps: FileScopeDeps,
): Promise<'all' | 'assigned' | 'none'> {
  if (!isStaffActor(viewer.actorType) || !deps.userPermissions) return 'none';
  return (await seesAllCustomers(viewer, deps.userPermissions)) ? 'all' : 'assigned';
}

async function clientCallsAccess(
  viewer: FileLibraryViewer,
  deps: FileScopeDeps,
): Promise<'all' | 'assigned' | 'none'> {
  if (!viewer.holdsCallRecordPermission || !isStaffActor(viewer.actorType)) return 'none';
  if (!deps.assignmentVisibilityEnabled) return 'all';
  if (deps.userPermissions && (await seesAllCustomers(viewer, deps.userPermissions))) return 'all';
  return 'assigned';
}

/** Sin repetidos, la actividad más reciente primero (sin actividad al final), el id como desempate y el tope. */
function capped(rows: readonly ScopeCandidate[]): ScopeList {
  const latest = new Map<string, number>();
  for (const row of rows) {
    const at = row.lastActivityAtUtc?.getTime() ?? Number.NEGATIVE_INFINITY;
    latest.set(row.id, Math.max(latest.get(row.id) ?? Number.NEGATIVE_INFINITY, at));
  }
  const ids = [...latest.entries()].sort(([a, x], [b, y]) => y - x || a.localeCompare(b)).map(([id]) => id);
  return { ids: ids.slice(0, FilesLimits.ScopeMaxIds), truncated: ids.length > FilesLimits.ScopeMaxIds };
}
