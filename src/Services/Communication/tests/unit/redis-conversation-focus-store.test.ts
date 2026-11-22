import { describe, expect, it } from 'vitest';
import type { Redis } from 'ioredis';
import { RedisConversationFocusStore } from '../../src/infrastructure/redis/redis-conversation-focus-store.js';

/** Redis en memoria con lo que usa el store (sin TTL real). */
class FakeRedis {
  readonly strings = new Map<string, string>();
  readonly sets = new Map<string, Set<string>>();

  async set(key: string, value: string): Promise<'OK'> {
    this.strings.set(key, value);
    return 'OK';
  }
  async del(key: string): Promise<number> {
    return this.strings.delete(key) ? 1 : 0;
  }
  async mget(...keys: string[]): Promise<Array<string | null>> {
    return keys.map((k) => this.strings.get(k) ?? null);
  }
  async sadd(key: string, member: string): Promise<number> {
    const set = this.sets.get(key) ?? new Set<string>();
    this.sets.set(key, set);
    const had = set.has(member);
    set.add(member);
    return had ? 0 : 1;
  }
  async srem(key: string, ...members: string[]): Promise<number> {
    const set = this.sets.get(key);
    if (!set) return 0;
    return members.filter((m) => set.delete(m)).length;
  }
  async smembers(key: string): Promise<string[]> {
    return [...(this.sets.get(key) ?? [])];
  }
  async expire(): Promise<number> {
    return 1;
  }
}

const tenantId = 't-1';
const lease = 90;

function store() {
  const redis = new FakeRedis();
  return { redis, focus: new RedisConversationFocusStore(redis as unknown as Redis) };
}

describe('RedisConversationFocusStore', () => {
  it('snapshot separates active from viewing', async () => {
    const { focus } = store();
    await focus.set({ tenantId, userId: 'A', sessionId: 's-a', visible: true, conversationId: 'X', leaseSeconds: lease });
    await focus.set({ tenantId, userId: 'B', sessionId: 's-b', visible: true, conversationId: null, leaseSeconds: lease });
    await focus.set({ tenantId, userId: 'C', sessionId: 's-c', visible: false, conversationId: 'X', leaseSeconds: lease });

    const snapshot = await focus.snapshot(tenantId, ['A', 'B', 'C', 'D'], 'X');

    expect(snapshot.active).toEqual(['A', 'B']);
    expect(snapshot.viewing).toEqual(['A']);
  });

  it('any visible session of the user counts', async () => {
    const { focus } = store();
    await focus.set({ tenantId, userId: 'A', sessionId: 'hidden', visible: false, conversationId: null, leaseSeconds: lease });
    await focus.set({ tenantId, userId: 'A', sessionId: 'shown', visible: true, conversationId: 'X', leaseSeconds: lease });

    expect(await focus.snapshot(tenantId, ['A'], 'X')).toEqual({ active: ['A'], viewing: ['A'] });
  });

  it('clear forgets the session', async () => {
    const { focus } = store();
    await focus.set({ tenantId, userId: 'A', sessionId: 's-a', visible: true, conversationId: 'X', leaseSeconds: lease });
    await focus.clear({ tenantId, userId: 'A', sessionId: 's-a' });

    expect(await focus.snapshot(tenantId, ['A'], 'X')).toEqual({ active: [], viewing: [] });
  });

  it('an expired session is ignored and pruned from the index', async () => {
    const { redis, focus } = store();
    await focus.set({ tenantId, userId: 'A', sessionId: 's-a', visible: true, conversationId: 'X', leaseSeconds: lease });
    redis.strings.clear(); // el lease venció

    expect(await focus.snapshot(tenantId, ['A'], 'X')).toEqual({ active: [], viewing: [] });
    expect([...redis.sets.values()].every((set) => set.size === 0)).toBe(true);
  });
});
