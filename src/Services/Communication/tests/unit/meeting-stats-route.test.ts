import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { registerMeetingRoutes } from '../../src/api/http/routes/meetings.route.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';
import {
  createFakeProjectionRepository,
  fakeSnapshot,
} from '../helpers/fake-user-permissions-projection-repository.js';

/** `GET /communication/meetings/stats`: "hoy" se corta en la zona de la oficina, no en la del usuario. */
const OFFICE = randomUUID();

interface StatsInput {
  readonly dayStartUtc: Date;
  readonly dayEndUtc: Date;
  readonly weekEndUtc: Date;
}

function setup(input: { officeTimeZone?: string; userZoneinfo?: string }) {
  const userId = randomUUID();
  const captured: StatsInput[] = [];
  const container = {
    userPermissions: createFakeProjectionRepository([
      fakeSnapshot({
        userId,
        tenantId: OFFICE,
        permissions: ['communication.meeting.join'],
        actorType: 'TenantEmployee',
      }),
    ]),
    tenantTimeZones: {
      async find(id: string) {
        return input.officeTimeZone
          ? {
              tenantId: id,
              timeZoneId: input.officeTimeZone,
              sourceVersion: 1,
              sourceUpdatedAtUtc: new Date(0),
            }
          : null;
      },
      async save() {},
    },
    meetings: {
      async getStatsForUser(stats: StatsInput) {
        captured.push(stats);
        return { today: 0, thisWeek: 0, liveNow: 0, transcriptsAvailable: 0 };
      },
    },
  } as unknown as AppContainer;

  const principal: AuthenticatedPrincipal = {
    userId,
    tenantId: OFFICE,
    actorType: 'TenantEmployee',
    permissions: [],
    permissionVersion: 1,
    sessionId: undefined,
    jti: undefined,
    raw: input.userZoneinfo ? { zoneinfo: input.userZoneinfo } : {},
  };

  async function get(url: string) {
    const app = Fastify();
    app.decorate('authenticate', async function authenticate(request: FastifyRequest, _reply: FastifyReply) {
      request.principal = principal;
    });
    await registerMeetingRoutes(app, container);
    await app.ready();
    const response = await app.inject({ method: 'GET', url });
    await app.close();
    return response;
  }

  return { captured, get };
}

afterEach(() => {
  vi.useRealTimers();
});

describe('GET /communication/meetings/stats', () => {
  it('corta hoy en la zona de la oficina, no en la del usuario', async () => {
    // 06:00 UTC: en Los Ángeles aún es el 20-nov (medianoche = 08:00 UTC); en Tokio ya es el 21.
    vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-11-21T06:00:00Z') });
    const { captured, get } = setup({ officeTimeZone: 'America/Los_Angeles', userZoneinfo: 'Asia/Tokyo' });

    const response = await get('/communication/meetings/stats');

    expect(response.statusCode).toBe(200);
    expect(captured).toHaveLength(1);
    expect(captured[0]?.dayStartUtc.toISOString()).toBe('2026-11-20T08:00:00.000Z');
  });

  it('sin zona de la oficina usa la medianoche UTC', async () => {
    vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-11-21T06:00:00Z') });
    const { captured, get } = setup({ userZoneinfo: 'Asia/Tokyo' });

    const response = await get('/communication/meetings/stats');

    expect(response.statusCode).toBe(200);
    expect(captured[0]?.dayStartUtc.toISOString()).toBe('2026-11-21T00:00:00.000Z');
  });
});
