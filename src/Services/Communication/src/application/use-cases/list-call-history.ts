import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { CallSnapshot } from '../../domain/calls/call.js';
import type { CallAudience, CallHistoryFilter, CallRepository } from '../ports/call-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';

export interface CallHistoryViewer {
  readonly tenantId: string;
  readonly userId: string;
  readonly actorType: string;
}

/** Una llamada vista por uno de sus participantes (pagina Calls del CRM y del portal). */
export interface CallHistoryItemDto {
  readonly id: string;
  readonly kind: string;
  readonly status: string;
  readonly callerUserId: string;
  readonly calleeUserId: string;
  readonly conversationId: string | null;
  readonly ringingAtUtc: string;
  readonly endedAtUtc: string | null;
  readonly durationSeconds: number | null;
  readonly recordingFileId: string | null;
  readonly endReason: string | null;
  readonly direction: 'incoming' | 'outgoing';
  readonly audience: CallAudience;
  /** El otro participante. Sin nombre si nunca entro (perdida o rechazada): el front lo resuelve. */
  readonly counterpart: { readonly userId: string; readonly displayName: string; readonly customerId: string | null };
  /** null = la llamada no se grabo. `viewable` ya aplica la regla del portal (visibleInPortal). */
  readonly recording: { readonly state: string; readonly transcriptReady: boolean; readonly viewable: boolean } | null;
}

export interface CallHistoryPage {
  readonly items: readonly CallHistoryItemDto[];
  readonly page: number;
  readonly size: number;
  readonly totalCount: number;
}

export interface ListCallHistoryDeps {
  readonly calls: Pick<CallRepository, 'listRecentForUser' | 'countRecentForUser' | 'findById'>;
  readonly recordingSessions: Pick<RecordingSessionRepository, 'listStatesByScopeIds'>;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
}

export async function listCallHistory(
  input: { viewer: CallHistoryViewer; page: number; size: number; filter?: CallHistoryFilter },
  deps: ListCallHistoryDeps,
): Promise<CallHistoryPage> {
  const { viewer } = input;
  const filter = input.filter ?? {};
  const [snapshots, totalCount] = await Promise.all([
    deps.calls.listRecentForUser({
      tenantId: viewer.tenantId,
      userId: viewer.userId,
      take: input.size,
      skip: (input.page - 1) * input.size,
      filter,
    }),
    deps.calls.countRecentForUser(viewer.tenantId, viewer.userId, filter),
  ]);
  const items = await toItems(viewer, snapshots, deps);
  return { items, page: input.page, size: input.size, totalCount };
}

/** Solo participantes (D-2): a los demas, NotFound para no revelar que la llamada existe. */
export async function getCallHistoryItem(
  input: { viewer: CallHistoryViewer; callId: string },
  deps: ListCallHistoryDeps,
): Promise<Result<CallHistoryItemDto>> {
  const call = await deps.calls.findById(input.viewer.tenantId, input.callId);
  if (!call || !call.isParticipant(input.viewer.userId)) {
    return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  }
  const [item] = await toItems(input.viewer, [call.toSnapshot()], deps);
  return item ? Result.ok(item) : Result.fail(makeError('Call.NotFound', 'Call not found.'));
}

async function toItems(
  viewer: CallHistoryViewer,
  snapshots: readonly CallSnapshot[],
  deps: ListCallHistoryDeps,
): Promise<CallHistoryItemDto[]> {
  if (snapshots.length === 0) return [];
  const states = await deps.recordingSessions.listStatesByScopeIds(
    viewer.tenantId,
    RecordingScope.Call,
    snapshots.map((s) => s.id),
  );
  // El cliente del portal ve la grabacion solo si la oficina la hace visible (D-29).
  const portalCanView =
    viewer.actorType === 'CustomerPortal' ? (await deps.effectiveSettings.resolve(viewer.tenantId)).recording.visibleInPortal : true;
  return snapshots.map((s) => toItem(viewer.userId, s, states.get(s.id) ?? null, portalCanView));
}

function toItem(userId: string, s: CallSnapshot, sessionState: string | null, portalCanView: boolean): CallHistoryItemDto {
  const other = s.participants.find((p) => p.userId !== userId) ?? null;
  // Grabaciones previas al recorder no tienen sesion: con el archivo basta para darla por lista.
  const state = sessionState ?? (s.recordingFileId ? 'Ready' : null);
  return {
    id: s.id,
    kind: s.kind,
    status: s.status,
    callerUserId: s.callerUserId,
    calleeUserId: s.calleeUserId,
    conversationId: s.conversationId,
    ringingAtUtc: s.ringingAtUtc.toISOString(),
    endedAtUtc: s.endedAtUtc ? s.endedAtUtc.toISOString() : null,
    durationSeconds: s.durationSeconds,
    recordingFileId: s.recordingFileId,
    endReason: s.endReason,
    direction: s.callerUserId === userId ? 'outgoing' : 'incoming',
    audience: s.participants.some((p) => p.customerId) ? 'client' : 'team',
    counterpart: other
      ? { userId: other.userId, displayName: other.displayName, customerId: other.customerId }
      : { userId: s.callerUserId === userId ? s.calleeUserId : s.callerUserId, displayName: '', customerId: null },
    recording: state
      ? { state, transcriptReady: s.transcriptFileId !== null, viewable: state === 'Ready' && portalCanView }
      : null,
  };
}
