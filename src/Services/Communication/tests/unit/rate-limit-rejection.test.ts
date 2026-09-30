import { describe, expect, it } from 'vitest';
import type { FastifyReply } from 'fastify';
import { RATE_LIMIT_CODE, rateLimitMessage, sendRateLimited } from '../../src/infrastructure/http/rate-limit-rejection.js';

function fakeReply() {
  const captured: { status?: number; headers: Record<string, string>; body?: unknown } = { headers: {} };
  const reply = {
    code(status: number) {
      captured.status = status;
      return reply;
    },
    header(name: string, value: string) {
      captured.headers[name] = value;
      return reply;
    },
    send(body: unknown) {
      captured.body = body;
      return reply;
    },
  };
  return { reply: reply as unknown as FastifyReply, captured };
}

describe('sendRateLimited', () => {
  it('responds 429 with Retry-After and the shared contract body', () => {
    const { reply, captured } = fakeReply();

    const returned = sendRateLimited(reply, 30, 'communication.global_http_ip');

    expect(returned).toBe(reply);
    expect(captured.status).toBe(429);
    expect(captured.headers['Retry-After']).toBe('30');
    expect(captured.body).toEqual({
      code: RATE_LIMIT_CODE,
      message: "You're making requests too quickly. Please try again in 30 seconds.",
      retryAfterSeconds: 30,
      policy: 'communication.global_http_ip',
    });
  });

  it('never tells the client to wait less than one second', () => {
    const { reply, captured } = fakeReply();

    sendRateLimited(reply, 0.2, 'communication.d.meeting_join_by_token');

    expect(captured.headers['Retry-After']).toBe('1');
    expect((captured.body as { retryAfterSeconds: number }).retryAfterSeconds).toBe(1);
  });
});

describe('rateLimitMessage', () => {
  it('uses the singular for one second', () => {
    expect(rateLimitMessage(1)).toBe("You're making requests too quickly. Please try again in 1 second.");
  });
});
