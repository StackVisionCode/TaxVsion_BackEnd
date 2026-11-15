import { afterEach, describe, expect, it, vi } from 'vitest';
import { HttpTenantDirectoryClient } from '../../src/infrastructure/platform/http-tenant-directory-client.js';
import type { ServiceTokenClient } from '../../src/infrastructure/auth/service-token-client.js';

const tokens = { getToken: async () => 'platform-token' } as unknown as ServiceTokenClient;

describe('tenant directory client', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sin URL configurada usa el Tenant local, igual que el kit .NET', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify([]), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await new HttpTenantDirectoryClient(tokens, undefined).listPage(1, 200);

    expect(fetchMock).toHaveBeenCalledWith('http://localhost:5217/internal/tenants/directory?page=1&size=200', {
      headers: { authorization: 'Bearer platform-token' },
    });
  });

  it('lee id y zona de cada oficina', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(JSON.stringify([{ id: 't-1', name: 'A', subdomain: 'a', defaultTimeZoneId: 'America/Chicago' }]), {
            status: 200,
          }),
      ),
    );

    const rows = await new HttpTenantDirectoryClient(tokens, 'http://tenant-api:8080/').listPage(1, 200);

    expect(rows).toEqual([{ tenantId: 't-1', timeZoneId: 'America/Chicago' }]);
  });
});
