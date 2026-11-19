import { Result, makeError } from '../../domain/shared/result.js';
import { SupportStatus } from '../../domain/support/support-enums.js';
import { normalizeTimeZoneId } from '../../domain/time-zones/tenant-time-zone.js';
import {
  WORK_DUE_SOON_DAYS,
  WorkKinds,
  WorkLimits,
  type WorkBatchResponse,
  type WorkCount,
  type WorkItem,
  type WorkItemsPage,
  type WorkSummaryResponse,
} from '../../contracts/http/work-http.js';
import type { AssignedTicketRow, MemberWorkReader, UpcomingMeetingRow } from '../ports/member-work-reader.js';
import type { TenantTimeZoneStore } from '../ports/tenant-time-zone-store.js';
import { localDate, wallToUtc } from '../services/office-clock.js';

export const WORK_PROVIDER = 'communication';

/**
 * Quién mira y qué puede leer de cada módulo. La autorización (propio vs `users.work.view`) la decide la ruta;
 * aquí solo se aplica la visibilidad de cada recurso para el que mira.
 */
export interface WorkViewer {
  readonly userId: string;
  readonly tenantId: string;
  readonly isPlatformAdmin: boolean;
  readonly canReadMeetings: boolean;
  /** Solo en la oficina de agentes (plataforma) y con `communication.support.agent`. */
  readonly canReadSupport: boolean;
}

export interface WorkWindow {
  readonly nowUtc: Date;
  /** Inicio en UTC del día hoy+7 de la oficina (exclusivo). */
  readonly dueSoonEndUtc: Date;
}

export const InvalidWorkUserIds = makeError(
  'Communication.Work.InvalidUserIds',
  'Send between 1 and 100 valid user ids separated by commas.',
);

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** `userIds=a,b,c` del lote: GUIDs, sin repetidos, entre 1 y 100 (mismo criterio que Calendar). */
export function parseWorkUserIds(raw: string | undefined): Result<string[]> {
  const ids: string[] = [];
  for (const part of (raw ?? '')
    .split(',')
    .map((p) => p.trim())
    .filter((p) => p.length > 0)) {
    if (!GUID.test(part)) return Result.fail(InvalidWorkUserIds);
    const id = part.toLowerCase();
    if (!ids.includes(id)) ids.push(id);
  }
  return ids.length === 0 || ids.length > WorkLimits.BatchUsers
    ? Result.fail(InvalidWorkUserIds)
    : Result.ok(ids);
}

/** Zona de la oficina desde la proyección local de Tenant; UTC si no hay fila o la base falla. */
export async function officeTimeZone(
  zones: Pick<TenantTimeZoneStore, 'find'>,
  tenantId: string,
): Promise<string> {
  const row = await zones.find(tenantId).catch(() => null);
  return normalizeTimeZoneId(row?.timeZoneId) ?? 'UTC';
}

/** Ventana "dueSoon" en la zona de la oficina: de ahora al inicio del día hoy+7 (días civiles, no bloques de 24 h). */
export function workWindow(nowUtc: Date, timeZone: string): WorkWindow {
  const today = localDate(nowUtc, timeZone);
  const end = new Date(Date.parse(`${today}T00:00:00Z`) + WORK_DUE_SOON_DAYS * 86_400_000)
    .toISOString()
    .slice(0, 10);
  // Con shiftForward nunca es null; el respaldo solo satisface al tipo.
  return {
    nowUtc,
    dueSoonEndUtc: wallToUtc(end, '00:00', timeZone, 'shiftForward') ?? new Date(`${end}T00:00:00Z`),
  };
}

export async function getMemberWork(
  input: { viewer: WorkViewer; userId: string; window: WorkWindow },
  deps: { memberWork: MemberWorkReader },
): Promise<WorkSummaryResponse> {
  const userId = input.userId.toLowerCase();
  const { tickets, meetings } = await readWork(input.viewer, [userId], input.window.nowUtc, deps.memberWork);

  const counts: WorkCount[] = [];
  const candidates: { item: WorkItem; at: number }[] = [];

  if (tickets) {
    const mine = tickets.filter((t) => same(t.assignedAgentId, userId));
    counts.push({
      kind: WorkKinds.SupportTickets,
      open: mine.length,
      overdue: 0,
      dueSoon: 0,
      waiting: mine.filter((t) => t.status === SupportStatus.WaitingCustomer).length,
    });
    candidates.push(...ticketCandidates(mine, input.viewer));
  }

  if (meetings) {
    const mine = meetings.filter((m) => isMember(m, userId));
    counts.push({
      kind: WorkKinds.Meetings,
      open: mine.length,
      overdue: 0,
      dueSoon: mine.filter((m) => isDueSoon(m, input.window)).length,
      waiting: 0,
    });
    candidates.push(...meetingCandidates(mine, input.viewer, input.window.nowUtc));
  }

  // Por fecha: los tickets más antiguos y después las reuniones más próximas.
  const top = byDate(candidates).slice(0, WorkLimits.TopItems);
  return { provider: WORK_PROVIDER, module: WORK_PROVIDER, counts, top };
}

/** Tipos que sirve la lista paginada de este proveedor. */
export const WorkItemsKinds = [WorkKinds.Meetings, WorkKinds.SupportTickets] as const;
export type WorkItemsKind = (typeof WorkItemsKinds)[number];

export const WorkKindNotReadable = makeError('Auth.Forbidden', 'You cannot read this kind of work.');

/**
 * "Show all" de un bloque: mismo universo y orden que el top (solo lo que el que mira puede abrir), así que
 * `totalCount` es lo visible y puede ser menor que el conteo `open` del bloque.
 */
export async function getMemberWorkItems(
  input: {
    viewer: WorkViewer;
    userId: string;
    kind: WorkItemsKind;
    page: number;
    size: number;
    nowUtc: Date;
  },
  deps: { memberWork: MemberWorkReader },
): Promise<Result<WorkItemsPage>> {
  const { viewer, kind, page, size } = input;
  const userId = input.userId.toLowerCase();
  let candidates: { item: WorkItem; at: number }[];
  if (kind === WorkKinds.SupportTickets) {
    if (!viewer.canReadSupport) return Result.fail(WorkKindNotReadable);
    const tickets = await deps.memberWork.listOpenAssignedTickets(viewer.tenantId, [userId]);
    candidates = ticketCandidates(
      tickets.filter((t) => same(t.assignedAgentId, userId)),
      viewer,
    );
  } else {
    if (!viewer.canReadMeetings) return Result.fail(WorkKindNotReadable);
    const meetings = await deps.memberWork.listUpcomingMeetings(viewer.tenantId, [userId], input.nowUtc);
    candidates = meetingCandidates(
      meetings.filter((m) => isMember(m, userId)),
      viewer,
      input.nowUtc,
    );
  }

  const items = byDate(candidates);
  const totalPages = Math.ceil(items.length / size);
  return Result.ok({
    items: items.slice((page - 1) * size, page * size),
    page,
    size,
    totalCount: items.length,
    totalPages,
    hasMore: page < totalPages,
    hasPrevious: page > 1,
  });
}

function ticketCandidates(
  mine: readonly AssignedTicketRow[],
  viewer: WorkViewer,
): { item: WorkItem; at: number }[] {
  return mine
    .filter((row) => canSeeTicket(row, viewer))
    .map((t) => ({ item: ticketItem(t), at: t.openedAtUtc.getTime() }));
}

// Una reunión que el que mira no ve en su listado solo suma al conteo: ni título ni enlace.
function meetingCandidates(
  mine: readonly UpcomingMeetingRow[],
  viewer: WorkViewer,
  nowUtc: Date,
): { item: WorkItem; at: number }[] {
  return mine
    .filter((row) => canSeeMeeting(row, viewer.userId))
    .map((m) => {
      const at = m.status === 'Live' ? null : m.scheduledForUtc;
      return { item: meetingItem(m), at: (at ?? nowUtc).getTime() };
    });
}

// Orden estable por fecha: en un empate queda el orden de la lectura.
function byDate(candidates: { item: WorkItem; at: number }[]): WorkItem[] {
  return [...candidates].sort((a, b) => a.at - b.at).map((c) => c.item);
}

export async function getMemberWorkBatch(
  input: { viewer: WorkViewer; userIds: readonly string[]; window: WorkWindow },
  deps: { memberWork: MemberWorkReader },
): Promise<WorkBatchResponse> {
  const userIds = input.userIds.map((id) => id.toLowerCase());
  const { tickets, meetings } = await readWork(input.viewer, userIds, input.window.nowUtc, deps.memberWork);
  return {
    rows: userIds.map((userId) => ({
      userId,
      open:
        (tickets?.filter((t) => same(t.assignedAgentId, userId)).length ?? 0) +
        (meetings?.filter((m) => isMember(m, userId)).length ?? 0),
      overdue: 0,
    })),
  };
}

async function readWork(
  viewer: WorkViewer,
  userIds: readonly string[],
  nowUtc: Date,
  reader: MemberWorkReader,
): Promise<{ tickets: AssignedTicketRow[] | null; meetings: UpcomingMeetingRow[] | null }> {
  // Siempre el tenant del JWT: otra oficina nunca entra en la consulta.
  const [tickets, meetings] = await Promise.all([
    viewer.canReadSupport ? reader.listOpenAssignedTickets(viewer.tenantId, userIds) : Promise.resolve(null),
    viewer.canReadMeetings
      ? reader.listUpcomingMeetings(viewer.tenantId, userIds, nowUtc)
      : Promise.resolve(null),
  ]);
  return { tickets, meetings };
}

function same(a: string | null | undefined, b: string): boolean {
  return !!a && a.toLowerCase() === b.toLowerCase();
}

/** Trabajo de la persona: host, participante no retirado o invitación abierta. */
function isMember(m: UpcomingMeetingRow, userId: string): boolean {
  return (
    same(m.hostUserId, userId) ||
    m.participants.some((p) => same(p.userId, userId) && p.status !== 'Removed') ||
    m.invitees.some((i) => same(i.userId, userId) && !i.revoked && !i.closed)
  );
}

/** Igual que `canSeeMeeting` del listado: host, participante o invitado no revocado. */
function canSeeMeeting(m: UpcomingMeetingRow, viewerId: string): boolean {
  return (
    same(m.hostUserId, viewerId) ||
    m.participants.some((p) => same(p.userId, viewerId)) ||
    m.invitees.some((i) => same(i.userId, viewerId) && !i.revoked)
  );
}

/** Igual que `SupportTicket.canBeAccessedBy` para un ticket asignado: su agente o PlatformAdmin. */
function canSeeTicket(t: AssignedTicketRow, viewer: WorkViewer): boolean {
  return viewer.isPlatformAdmin || same(t.assignedAgentId, viewer.userId);
}

function isDueSoon(m: UpcomingMeetingRow, window: WorkWindow): boolean {
  const at = m.status === 'Scheduled' ? m.scheduledForUtc : null;
  return !!at && at >= window.nowUtc && at < window.dueSoonEndUtc;
}

function ticketItem(t: AssignedTicketRow): WorkItem {
  return {
    id: t.id,
    kind: WorkKinds.SupportTickets,
    title: t.subject,
    customerId: null,
    dueAtUtc: null,
    dueDate: null,
    status: t.status,
    link: `/support?ticketId=${t.id}`,
  };
}

function meetingItem(m: UpcomingMeetingRow): WorkItem {
  return {
    id: m.id,
    kind: WorkKinds.Meetings,
    title: m.title,
    customerId: null,
    dueAtUtc: m.scheduledForUtc?.toISOString() ?? null,
    dueDate: null,
    status: m.status,
    link: `/meetings?meetingId=${m.id}`,
  };
}
