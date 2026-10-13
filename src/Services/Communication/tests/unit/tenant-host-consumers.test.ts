import { describe, expect, it } from 'vitest';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { TenantHostCache } from '../../src/application/ports/tenant-host-cache.js';
import {
  TenantHostEventTypes,
  bindTenantHostConsumers,
} from '../../src/application/event-handlers/tenant-host-consumers.js';

class RecordingHostCache implements TenantHostCache {
  readonly forgotten: string[] = [];
  forgotAll = 0;
  forget(tenantId: string): void {
    this.forgotten.push(tenantId);
  }
  forgetAll(): void {
    this.forgotAll++;
  }
}

function bind(cache: TenantHostCache): Map<string, (env: IncomingEnvelope) => Promise<void>> {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  bindTenantHostConsumers((type, handler) => handlers.set(type, handler), { hostCache: cache });
  return handlers;
}

function envelope(eventType: string, tenantId: string): IncomingEnvelope {
  return { eventId: 'e1', eventType, tenantId, occurredOnUtc: new Date().toISOString(), payload: {} };
}

describe('tenant host consumers', () => {
  it('olvida el host de la oficina que renombró su subdominio', async () => {
    const cache = new RecordingHostCache();
    const handlers = bind(cache);

    await handlers.get(TenantHostEventTypes.SubdomainChanged)!(envelope(TenantHostEventTypes.SubdomainChanged, 't-1'));

    expect(cache.forgotten).toEqual(['t-1']);
    expect(cache.forgotAll).toBe(0);
  });

  it('olvida todos los hosts cuando cambia el dominio de la plataforma', async () => {
    const cache = new RecordingHostCache();
    const handlers = bind(cache);

    await handlers.get(TenantHostEventTypes.PlatformSettingsChanged)!(
      envelope(TenantHostEventTypes.PlatformSettingsChanged, 'platform'),
    );

    expect(cache.forgotAll).toBe(1);
  });
});
