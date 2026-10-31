import { describe, expect, it, vi } from 'vitest';
import Fastify, { type FastifyRequest } from 'fastify';
import { requirePlatformPermission } from '../../src/api/http/require-platform-permission.js';
import { registerAdminAnalyticsRoutes } from '../../src/api/http/routes/admin-analytics.route.js';
import { CommunicationPermissions } from '../../src/domain/shared/permissions.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';
import type { AppContainer } from '../../src/infrastructure/container.js';

const userPermissions = { findByUserId: vi.fn().mockResolvedValue(null) } as never;

function principal(actorType: string): AuthenticatedPrincipal {
  return { userId: '11111111-1111-4111-8111-111111111111', tenantId: '22222222-2222-4222-8222-222222222222', actorType } as AuthenticatedPrincipal;
}

/** F10 (ADD-08 §14.1): la analítica de plataforma es PlatformOnly; un Tenant Admin no entra ni con bypass. */
describe('requirePlatformPermission', () => {
  it('un Tenant Admin recibe la decisión 403 Auth.PlatformOnly', async () => {
    const denied = await requirePlatformPermission(principal('TenantAdmin'), CommunicationPermissions.PlatformAnalyticsRead, { userPermissions });
    expect(denied).toEqual({ status: 403, body: expect.objectContaining({ code: 'Auth.PlatformOnly' }) });
  });

  it('un PlatformAdmin pasa', async () => {
    expect(await requirePlatformPermission(principal('PlatformAdmin'), CommunicationPermissions.PlatformAnalyticsRead, { userPermissions })).toBeNull();
  });

  // Regresión: el guard devolvía el `reply` (thenable) y el `await` lo convertía en undefined, así que
  // la ruta seguía tras el 403 — leía datos, auditaba y en PUT platform-settings llegaba a guardar.
  it('tras el 403 la ruta no sigue: no lee el snapshot ni audita', async () => {
    const listAllForRange = vi.fn().mockResolvedValue([]);
    const append = vi.fn().mockResolvedValue(undefined);
    const app = Fastify();
    app.decorateRequest('principal', undefined);
    app.decorate('authenticate', async (request: FastifyRequest) => {
      request.principal = principal('TenantAdmin');
    });
    await registerAdminAnalyticsRoutes(
      app,
      {
        analytics: { listAllForRange },
        configurationAudit: { append },
        userPermissions,
        effectiveSettings: { platform: async () => ({ analyticsTimeZone: 'UTC' }) },
      } as unknown as AppContainer,
      { defaultRangeDays: 30, maxRangeDays: 400, tenantsMaxLimit: 100, grafanaUrl: null },
    );
    const res = await app.inject({ method: 'GET', url: '/communication/admin/analytics/summary' });
    await new Promise((r) => setTimeout(r, 20));
    expect(res.statusCode).toBe(403);
    expect(res.json()).toMatchObject({ code: 'Auth.PlatformOnly' });
    expect(listAllForRange).not.toHaveBeenCalled();
    expect(append).not.toHaveBeenCalled();
    await app.close();
  });
});
