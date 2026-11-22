import { describe, expect, it } from 'vitest';
import {
  isNewMissedStreak,
  processMissedMessages,
} from '../../src/application/use-cases/process-missed-messages.js';
import type {
  MissedMessagesCandidate,
  MissedMessagesStore,
} from '../../src/application/ports/missed-messages-store.js';
import type { ChatMessagesMissedEvent } from '../../src/contracts/events/chat-events.js';

const minutes = (n: number) => n * 60_000;
const T0 = new Date('2026-11-22T10:00:00Z');
const at = (offsetMinutes: number) => new Date(T0.getTime() + minutes(offsetMinutes));

/** Store en memoria: devuelve las filas tal cual y aplica la marca como lo haría la base. */
class FakeStore implements MissedMessagesStore {
  readonly published: ChatMessagesMissedEvent[] = [];
  constructor(public rows: MissedMessagesCandidate[]) {}
  async findCandidates(input: { unreadBeforeUtc: Date }): Promise<readonly MissedMessagesCandidate[]> {
    return this.rows.filter((r) => r.oldestUnreadAtUtc <= input.unreadBeforeUtc);
  }
  async recordNotice(input: { candidate: MissedMessagesCandidate; noticedAtUtc: Date; event: ChatMessagesMissedEvent }) {
    const row = this.rows.find(
      (r) => r.conversationId === input.candidate.conversationId && r.userId === input.candidate.userId,
    )!;
    row.missedNoticeAtUtc = input.noticedAtUtc;
    this.published.push(input.event);
    return true;
  }
}

function row(patch: Partial<MissedMessagesCandidate> = {}): MissedMessagesCandidate {
  return {
    tenantId: 't-1',
    conversationId: 'c-1',
    conversationKind: 'Direct',
    userId: 'u-1',
    unreadCount: 1,
    oldestUnreadAtUtc: at(0),
    latestMessageId: 'm-1',
    latestSenderId: 'u-2',
    lastReadAtUtc: null,
    missedNoticeAtUtc: null,
    ...patch,
  };
}

describe('processMissedMessages', () => {
  it('publishes once per unread streak', async () => {
    const store = new FakeStore([row()]);
    const run = (now: Date) => processMissedMessages({ now, afterMinutes: 5 }, { store });

    expect(await run(at(6))).toBe(1);
    expect(store.published[0]).toMatchObject({
      eventType: 'communication.chat.messages_missed.v1',
      tenantId: 't-1',
      conversationId: 'c-1',
      recipientUserId: 'u-1',
      unreadCount: 1,
      conversationKind: 'Direct',
    });

    // Segunda pasada sin cambios: nada.
    expect(await run(at(7))).toBe(0);

    // Llega otro mensaje y pasan 5 min, pero la racha anterior sigue sin leer: nada.
    store.rows[0] = { ...store.rows[0]!, unreadCount: 2, latestMessageId: 'm-2' };
    expect(await run(at(20))).toBe(0);

    // Lee y llega otro: racha nueva.
    store.rows[0] = { ...store.rows[0]!, lastReadAtUtc: at(21), oldestUnreadAtUtc: at(22), unreadCount: 1, latestMessageId: 'm-3' };
    expect(await run(at(28))).toBe(1);
    expect(store.published).toHaveLength(2);
  });

  it('does nothing when the feature is off', async () => {
    const store = new FakeStore([row()]);

    expect(await processMissedMessages({ now: at(30), afterMinutes: 0 }, { store })).toBe(0);
    expect(store.published).toEqual([]);
  });

  it('a streak is new only after the user read past the last notice', () => {
    expect(isNewMissedStreak(row())).toBe(true);
    expect(isNewMissedStreak(row({ missedNoticeAtUtc: at(5), oldestUnreadAtUtc: at(6), lastReadAtUtc: null }))).toBe(false);
    expect(isNewMissedStreak(row({ missedNoticeAtUtc: at(5), oldestUnreadAtUtc: at(7), lastReadAtUtc: at(6) }))).toBe(true);
    expect(isNewMissedStreak(row({ missedNoticeAtUtc: at(5), oldestUnreadAtUtc: at(1), lastReadAtUtc: at(6) }))).toBe(false);
  });
});
