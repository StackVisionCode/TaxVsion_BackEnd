import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';

/**
 * Notification in-app. NO representa "session revoked" ni "force logout" —
 * esos son eventos socket dedicados (separacion explicita respecto al legacy
 * que los mezclaba en el mismo canal). Aqui solo mensajes de negocio: recordatorios
 * de firma, bulk import completado, etc.
 */
export const NotificationPriority = {
  Low: 'Low',
  Normal: 'Normal',
  High: 'High',
  Urgent: 'Urgent',
} as const;
export type NotificationPriority = (typeof NotificationPriority)[keyof typeof NotificationPriority];

export function isPriority(value: string): value is NotificationPriority {
  return value === 'Low' || value === 'Normal' || value === 'High' || value === 'Urgent';
}

export interface NotificationSnapshot {
  readonly id: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly kind: string;
  readonly priority: NotificationPriority;
  readonly title: string;
  readonly body: string;
  readonly metadata: Readonly<Record<string, unknown>>;
  readonly sourceEventId: string;
  readonly sourceEventType: string;
  readonly correlationId: string | null;
  readonly readAtUtc: Date | null;
  readonly dismissedAtUtc: Date | null;
  readonly createdAtUtc: Date;
}

export class Notification {
  private constructor(private state: NotificationSnapshot) {}

  static rehydrate(snapshot: NotificationSnapshot): Notification {
    return new Notification(snapshot);
  }

  static create(input: {
    tenantId: string;
    userId: string;
    kind: string;
    priority?: NotificationPriority;
    title: string;
    body: string;
    metadata?: Readonly<Record<string, unknown>>;
    sourceEventId: string;
    sourceEventType: string;
    correlationId?: string | null;
    now?: Date;
  }): Result<Notification> {
    if (input.title.trim().length === 0) {
      return Result.fail(makeError('Notification.MissingTitle', 'Title is required.'));
    }
    if (input.body.trim().length === 0) {
      return Result.fail(makeError('Notification.MissingBody', 'Body is required.'));
    }
    return Result.ok(
      new Notification({
        id: randomUUID(),
        tenantId: input.tenantId,
        userId: input.userId,
        kind: input.kind,
        priority: input.priority ?? NotificationPriority.Normal,
        title: input.title.trim().slice(0, 200),
        body: input.body.trim().slice(0, 1000),
        metadata: input.metadata ?? {},
        sourceEventId: input.sourceEventId,
        sourceEventType: input.sourceEventType,
        correlationId: input.correlationId ?? null,
        readAtUtc: null,
        dismissedAtUtc: null,
        createdAtUtc: input.now ?? new Date(),
      }),
    );
  }

  markRead(now: Date = new Date()): void {
    if (this.state.readAtUtc !== null) return;
    this.state = { ...this.state, readAtUtc: now };
  }

  dismiss(now: Date = new Date()): void {
    if (this.state.dismissedAtUtc !== null) return;
    this.state = { ...this.state, dismissedAtUtc: now };
  }

  toSnapshot(): NotificationSnapshot {
    return this.state;
  }

  get id(): string {
    return this.state.id;
  }
  get tenantId(): string {
    return this.state.tenantId;
  }
  get userId(): string {
    return this.state.userId;
  }
}
