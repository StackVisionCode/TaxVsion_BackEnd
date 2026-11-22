import type { Redis } from 'ioredis';
import type { ConversationFocusStore } from '../../application/ports/conversation-focus-store.js';

/**
 * Foco en Redis:
 *   - `comm:focus:t:{tenant}:u:{user}:s:{session}` = `{"v":1|0,"c":"<conversación>"|""}` con lease.
 *   - `comm:focus:idx:t:{tenant}:u:{user}` = SET con las sesiones del usuario (evita SCAN).
 * Una sesión vencida se ignora y se quita del índice al leerla.
 */
export class RedisConversationFocusStore implements ConversationFocusStore {
  constructor(private readonly redis: Redis) {}

  async set(input: {
    tenantId: string;
    userId: string;
    sessionId: string;
    visible: boolean;
    conversationId: string | null;
    leaseSeconds: number;
  }): Promise<void> {
    const value = JSON.stringify({ v: input.visible ? 1 : 0, c: input.conversationId ?? '' });
    await this.redis.set(this.sessionKey(input), value, 'EX', input.leaseSeconds);
    const index = this.indexKey(input.tenantId, input.userId);
    await this.redis.sadd(index, input.sessionId);
    await this.redis.expire(index, input.leaseSeconds);
  }

  async clear(input: { tenantId: string; userId: string; sessionId: string }): Promise<void> {
    await this.redis.del(this.sessionKey(input));
    await this.redis.srem(this.indexKey(input.tenantId, input.userId), input.sessionId);
  }

  async snapshot(
    tenantId: string,
    userIds: readonly string[],
    conversationId: string,
  ): Promise<{ active: readonly string[]; viewing: readonly string[] }> {
    const active: string[] = [];
    const viewing: string[] = [];
    for (const userId of userIds) {
      const index = this.indexKey(tenantId, userId);
      const sessions = await this.redis.smembers(index);
      if (sessions.length === 0) continue;
      const values = await this.redis.mget(...sessions.map((sessionId) => this.sessionKey({ tenantId, userId, sessionId })));
      const expired = sessions.filter((_, i) => values[i] === null);
      if (expired.length > 0) await this.redis.srem(index, ...expired);
      const states = values.flatMap((raw) => (raw === null ? [] : [parseFocus(raw)]));
      if (states.some((s) => s.visible)) active.push(userId);
      if (states.some((s) => s.visible && s.conversationId === conversationId)) viewing.push(userId);
    }
    return { active, viewing };
  }

  private sessionKey(input: { tenantId: string; userId: string; sessionId: string }): string {
    return `comm:focus:t:${input.tenantId}:u:${input.userId}:s:${input.sessionId}`;
  }

  private indexKey(tenantId: string, userId: string): string {
    return `comm:focus:idx:t:${tenantId}:u:${userId}`;
  }
}

function parseFocus(raw: string): { visible: boolean; conversationId: string | null } {
  try {
    const parsed = JSON.parse(raw) as { v?: number; c?: string };
    return { visible: parsed.v === 1, conversationId: parsed.c ? parsed.c : null };
  } catch {
    return { visible: false, conversationId: null };
  }
}
