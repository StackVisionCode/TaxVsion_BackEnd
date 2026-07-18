import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Notification } from '../../src/domain/notifications/notification.js';

function u(): string {
  return randomUUID();
}

describe('Notification.create', () => {
  it('creates a fresh unread notification with default Normal priority', () => {
    const r = Notification.create({
      tenantId: u(),
      userId: u(),
      kind: 'signature.reminder_due',
      title: 'Recordatorio',
      body: 'Firma pendiente.',
      sourceEventId: u(),
      sourceEventType: 'signature.request.reminder_due.v1',
    });
    expect(r.isSuccess).toBe(true);
    if (!r.isSuccess) return;
    const snap = r.value.toSnapshot();
    expect(snap.priority).toBe('Normal');
    expect(snap.readAtUtc).toBeNull();
    expect(snap.dismissedAtUtc).toBeNull();
  });

  it('rejects empty title', () => {
    const r = Notification.create({
      tenantId: u(),
      userId: u(),
      kind: 'x',
      title: '   ',
      body: 'x',
      sourceEventId: u(),
      sourceEventType: 'x',
    });
    expect(r.isSuccess).toBe(false);
    if (r.isSuccess) return;
    expect(r.error.code).toBe('Notification.MissingTitle');
  });

  it('trims and caps title to 200 chars', () => {
    const r = Notification.create({
      tenantId: u(),
      userId: u(),
      kind: 'x',
      title: 'x'.repeat(500),
      body: 'body',
      sourceEventId: u(),
      sourceEventType: 'x',
    });
    if (!r.isSuccess) throw new Error();
    expect(r.value.toSnapshot().title).toHaveLength(200);
  });
});

describe('Notification.markRead / dismiss', () => {
  function fresh() {
    const r = Notification.create({
      tenantId: u(),
      userId: u(),
      kind: 'x',
      title: 'title',
      body: 'body',
      sourceEventId: u(),
      sourceEventType: 'x',
    });
    if (!r.isSuccess) throw new Error();
    return r.value;
  }

  it('markRead is idempotent', () => {
    const n = fresh();
    const now = new Date();
    n.markRead(now);
    const readAt1 = n.toSnapshot().readAtUtc;
    n.markRead(new Date(now.getTime() + 1000));
    expect(n.toSnapshot().readAtUtc).toEqual(readAt1);
  });

  it('dismiss is idempotent', () => {
    const n = fresh();
    n.dismiss();
    const at1 = n.toSnapshot().dismissedAtUtc;
    n.dismiss();
    expect(n.toSnapshot().dismissedAtUtc).toEqual(at1);
  });
});
