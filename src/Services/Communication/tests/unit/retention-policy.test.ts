import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PlatformCommunicationSettings } from '../../src/domain/settings/platform-communication-settings.js';
import { TenantCommunicationConstraints } from '../../src/domain/settings/tenant-communication-constraints.js';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';
import { resolveEffectiveSettings } from '../../src/domain/settings/effective-communication-settings.js';

const actor = { userId: randomUUID() };

function effective(platformPatch: Record<string, unknown> = {}, constraintsPatch: Record<string, unknown> | null = null) {
  const tenantId = randomUUID();
  const platform = PlatformCommunicationSettings.defaults();
  if (Object.keys(platformPatch).length) {
    const updated = platform.update(platformPatch as never, actor);
    if (!updated.isSuccess) throw new Error(updated.error.message);
  }
  let constraints = null;
  if (constraintsPatch) {
    const c = TenantCommunicationConstraints.empty(tenantId);
    const updated = c.update(constraintsPatch as never, actor);
    if (!updated.isSuccess) throw new Error(updated.error.message);
    constraints = c.toSnapshot();
  }
  return resolveEffectiveSettings({
    platform: platform.toSnapshot(),
    limits: null,
    constraints,
    tenant: TenantCommunicationSettings.defaults(tenantId).toSnapshot(),
  });
}

describe('Retención de mensajes y grabaciones (la fija la plataforma, no la oficina)', () => {
  it('por defecto: 365 días de mensajes, 90 de grabaciones y sin purga', () => {
    expect(effective().retention).toEqual({ messageDays: 365, recordingDays: 90, purgeEnabled: false });
  });

  it('la global de PlatformAdmin rige para todas las oficinas, también en la sección de grabación', () => {
    const e = effective({ messageRetentionDays: 2555, recordingRetentionDays: 400, purgeEnabled: true });
    expect(e.retention).toEqual({ messageDays: 2555, recordingDays: 400, purgeEnabled: true });
    expect(e.recording.retentionDays).toBe(400);
    expect(e.recording.purgeEnabled).toBe(true);
  });

  it('la excepción por oficina manda sobre la global; null vuelve a la global', () => {
    const e = effective({ messageRetentionDays: 2555, purgeEnabled: true }, { messageRetentionDays: 3650, purgeEnabled: false });
    expect(e.retention).toEqual({ messageDays: 3650, recordingDays: 90, purgeEnabled: false });
    const back = effective({ messageRetentionDays: 2555 }, { messageRetentionDays: null });
    expect(back.retention.messageDays).toBe(2555);
  });

  it('fuera del rango técnico (1–3650 días) se rechaza en la global y en la excepción', () => {
    const platform = PlatformCommunicationSettings.defaults();
    expect(platform.update({ messageRetentionDays: 0 } as never, actor).isSuccess).toBe(false);
    const c = TenantCommunicationConstraints.empty(randomUUID());
    expect(c.update({ recordingRetentionDays: 5000 } as never, actor).isSuccess).toBe(false);
  });

  it('la configuración de la oficina ya no tiene retención', () => {
    const snapshot = TenantCommunicationSettings.defaults(randomUUID()).toSnapshot();
    expect('messageRetentionDays' in snapshot || 'recordingRetentionDays' in snapshot || 'purgeEnabled' in snapshot).toBe(false);
  });
});
