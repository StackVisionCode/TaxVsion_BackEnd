import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { HttpPlanRateLimitReader } from '../../src/infrastructure/rate-limit/http-plan-rate-limit-reader.js';
import type { ServiceTokenClient } from '../../src/infrastructure/auth/service-token-client.js';

const tokens = { getToken: async () => 'service-token' } as unknown as ServiceTokenClient;

function okResponse(multiplierOverride: number): Response {
  return new Response(JSON.stringify([{ planCode: 'pro', category: 'O', multiplierOverride, hardOverridePerMinute: null }]), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  });
}

/**
 * Antes un fallo dejaba un catalogo VACIO cacheado 5 min: todos los tenants caian a la cuota base aunque
 * Subscription volviera enseguida.
 */
describe('HttpPlanRateLimitReader', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-09-25T12:00:00Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('keeps serving the last good catalog when a refresh fails', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(okResponse(3)).mockResolvedValueOnce(new Response('', { status: 503 }));
    vi.stubGlobal('fetch', fetchMock);
    const reader = new HttpPlanRateLimitReader(tokens);

    await expect(reader.getMultiplier('pro', 'O')).resolves.toEqual({ multiplierOverride: 3, hardOverridePerMinute: null });

    vi.advanceTimersByTime(5 * 60 * 1000 + 1); // vence el catálogo → el refresco falla
    await expect(reader.getMultiplier('pro', 'O')).resolves.toEqual({ multiplierOverride: 3, hardOverridePerMinute: null });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('retries 30 seconds after a failure instead of pinning an empty catalog for 5 minutes', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response('', { status: 503 })).mockResolvedValueOnce(okResponse(5));
    vi.stubGlobal('fetch', fetchMock);
    const reader = new HttpPlanRateLimitReader(tokens);

    await expect(reader.getMultiplier('pro', 'O')).resolves.toBeNull();

    vi.advanceTimersByTime(10 * 1000);
    await expect(reader.getMultiplier('pro', 'O')).resolves.toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(1); // dentro del backoff no reintenta en cada llamada

    vi.advanceTimersByTime(20 * 1000 + 1);
    await expect(reader.getMultiplier('pro', 'O')).resolves.toEqual({ multiplierOverride: 5, hardOverridePerMinute: null });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
