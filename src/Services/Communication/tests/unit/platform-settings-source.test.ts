import { afterEach, describe, expect, it, vi } from 'vitest';
import { PlatformSettingsSource } from '../../src/infrastructure/platform/platform-settings-source.js';
import {
  TenantHostEventTypes,
  bindTenantHostConsumers,
} from '../../src/application/event-handlers/tenant-host-consumers.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';

describe('PlatformSettingsSource', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('arranca con el dominio de config y deriva los orígenes de las apps de la plataforma', () => {
    const source = new PlatformSettingsSource(' TaxOffix.io. ', undefined);

    expect(source.webOrigins()).toEqual([
      'https://taxoffix.io',
      'https://www.taxoffix.io',
      'https://app.taxoffix.io',
      'https://client.taxoffix.io',
    ]);
  });

  it('sin dominio no acepta ningún origen de plataforma', () => {
    expect(new PlatformSettingsSource('', undefined).webOrigins()).toEqual([]);
  });

  it('una versión nueva mueve los orígenes; una vieja no pisa a la nueva', () => {
    const source = new PlatformSettingsSource('taxoffix.io', undefined);

    expect(source.apply({ version: 3, baseDomain: 'taxoffix.com', brandName: 'TaxOffix' })).toBe(true);
    expect(source.apply({ version: 2, baseDomain: 'old.io', brandName: 'Old' })).toBe(false);

    expect(source.webOrigins()[0]).toBe('https://taxoffix.com');
  });

  it('lee la versión de Tenant y la aplica', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ version: 7, baseDomain: 'taxoffix.com', brandName: 'TaxOffix' }))),
    );
    const source = new PlatformSettingsSource('taxoffix.io', 'http://tenant-api:8080/');

    expect(await source.refreshOnce()).toBe(true);
    expect(source.snapshot.version).toBe(7);
    expect(source.webOrigins()).toContain('https://app.taxoffix.com');
  });

  it('si Tenant no contesta conserva la versión vigente', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 503 })));
    const source = new PlatformSettingsSource('taxoffix.io', 'http://tenant-api:8080');

    expect(await source.refreshOnce()).toBe(false);
    expect(source.snapshot.baseDomain).toBe('taxoffix.io');
  });

  it('el evento de cambio de settings aplica la versión nueva', async () => {
    const source = new PlatformSettingsSource('taxoffix.io', undefined);
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    bindTenantHostConsumers((type, handler) => handlers.set(type, handler), {
      hostCache: { forget: () => undefined, forgetAll: () => undefined },
      platformSettings: source,
    });

    await handlers.get(TenantHostEventTypes.PlatformSettingsChanged)!({
      eventId: 'e1',
      eventType: TenantHostEventTypes.PlatformSettingsChanged,
      tenantId: 'platform',
      occurredOnUtc: new Date().toISOString(),
      payload: { version: 2, baseDomain: 'taxoffix.com', brandName: 'TaxOffix' },
    });

    expect(source.webOrigins()).toContain('https://client.taxoffix.com');
  });
});
