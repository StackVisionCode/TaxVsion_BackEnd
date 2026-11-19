/**
 * Espejo de BuildingBlocks.Contracts.Work (C#): misma forma JSON en camelCase. El CRM compone un bloque por
 * proveedor; un cambio aquí debe reflejarse allá.
 */

/** Tipos de trabajo estables (kebab-case): el CRM los usa como clave, no se renombran. */
export const WorkKinds = {
  Tasks: 'tasks',
  ClientRequests: 'client-requests',
  FormRequests: 'form-requests',
  Appointments: 'appointments',
  Meetings: 'meetings',
  Signatures: 'signatures',
  SupportTickets: 'support-tickets',
  ClientsPreparer: 'clients-preparer',
  ClientsAssigned: 'clients-assigned',
} as const;
export type WorkKind = (typeof WorkKinds)[keyof typeof WorkKinds];

export const WorkLimits = {
  TopItems: 5,
  BatchUsers: 100,
} as const;

/** Ventana "dueSoon": hoy y los 6 días siguientes de la oficina (WorkDue.DueSoonDays). */
export const WORK_DUE_SOON_DAYS = 7;

export interface WorkCount {
  readonly kind: WorkKind;
  readonly open: number;
  readonly overdue: number;
  readonly dueSoon: number;
  readonly waiting: number;
}

/** `dueDate` (yyyy-MM-dd) para vencimientos civiles, `dueAtUtc` (ISO) para instantes. `link` = ruta del CRM. */
export interface WorkItem {
  readonly id: string;
  readonly kind: WorkKind;
  readonly title: string;
  readonly customerId: string | null;
  readonly dueAtUtc: string | null;
  readonly dueDate: string | null;
  readonly status: string;
  readonly link: string;
}

export interface WorkSummaryResponse {
  readonly provider: string;
  readonly module: string;
  readonly counts: readonly WorkCount[];
  readonly top: readonly WorkItem[];
}

export interface WorkBatchRow {
  readonly userId: string;
  readonly open: number;
  readonly overdue: number;
}

export interface WorkBatchResponse {
  readonly rows: readonly WorkBatchRow[];
}
