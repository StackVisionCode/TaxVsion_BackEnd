import type { Redis } from 'ioredis';
import { consumeWithinLimit } from './rate-counter.js';
import { clampToTechnical } from '../../domain/settings/technical-limits.js';
import type { MeetingReactionLimiter } from '../../application/ports/meeting-reaction-limiter.js';

const USER_WINDOW_SECONDS = 10;
const MEETING_WINDOW_SECONDS = 60;

/** F5 (D-50): ventanas fijas en Redis por (tenant, meeting, usuario) y por (tenant, meeting). */
export class RedisMeetingReactionLimiter implements MeetingReactionLimiter {
  constructor(private readonly redis: Redis) {}

  async consumeForUser(input: { tenantId: string; meetingId: string; userId: string; maxPer10s: number }): Promise<boolean> {
    const key = `comm:react:u:${input.tenantId}:${input.meetingId}:${input.userId}`;
    const result = await consumeWithinLimit(
      this.redis,
      key,
      USER_WINDOW_SECONDS,
      clampToTechnical('reactionsPerUserPer10s', input.maxPer10s),
    );
    return result.allowed;
  }

  async consumeForMeeting(input: { tenantId: string; meetingId: string; maxPerMinute: number }): Promise<boolean> {
    const key = `comm:react:m:${input.tenantId}:${input.meetingId}`;
    const result = await consumeWithinLimit(
      this.redis,
      key,
      MEETING_WINDOW_SECONDS,
      clampToTechnical('reactionsPerMeetingPerMinute', input.maxPerMinute),
    );
    return result.allowed;
  }
}
