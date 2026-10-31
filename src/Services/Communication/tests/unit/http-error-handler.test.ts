import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { registerHttpErrorHandler } from '../../src/infrastructure/http/http-error-handler.js';
import { registerMeetingRoutes } from '../../src/api/http/routes/meetings.route.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';

const PLATFORM_ADMIN: AuthenticatedPrincipal = {
  userId: randomUUID(),
  tenantId: randomUUID(),
  actorType: 'PlatformAdmin',
  permissions: [],
  permissionVersion: 1,
  sessionId: undefined,
  jti: undefined,
  raw: {},
};

async function serve(extra?: (app: ReturnType<typeof Fastify>) => void) {
  const app = Fastify();
  registerHttpErrorHandler(app);
  app.decorate('authenticate', async function authenticate(req: FastifyRequest, _reply: FastifyReply) {
    req.principal = PLATFORM_ADMIN;
  });
  // Las rutas reales de meetings: el id inválido falla en `IdParams.parse` antes de tocar el contenedor.
  await registerMeetingRoutes(app, {} as AppContainer);
  extra?.(app);
  await app.ready();
  return app;
}

describe('registerHttpErrorHandler', () => {
  it('un id que no es UUID en la ruta responde 400 con el campo, no 500', async () => {
    const app = await serve();
    for (const url of ['/communication/meetings/abc/summary', '/communication/meetings/not-a-uuid/events']) {
      const response = await app.inject({ method: 'GET', url });
      expect(response.statusCode).toBe(400);
      expect(response.json()).toMatchObject({ code: 'Request.Invalid', errors: [{ path: 'id' }] });
    }
    await app.close();
  });

  it('un 4xx propio conserva su código y un fallo inesperado es 500 sin detalles', async () => {
    const app = await serve((a) => {
      a.get('/teapot', async () => {
        throw Object.assign(new Error('No coffee.'), { statusCode: 418, code: 'Tea.Only' });
      });
      a.get('/boom', async () => {
        throw new Error('secret stack detail');
      });
    });

    const teapot = await app.inject({ method: 'GET', url: '/teapot' });
    expect(teapot.statusCode).toBe(418);
    expect(teapot.json()).toEqual({ code: 'Tea.Only', message: 'No coffee.' });

    const boom = await app.inject({ method: 'GET', url: '/boom' });
    expect(boom.statusCode).toBe(500);
    expect(boom.body).not.toContain('secret stack detail');
    await app.close();
  });
});
