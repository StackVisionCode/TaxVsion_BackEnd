import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PlatformCommunicationSettings } from '../../src/domain/settings/platform-communication-settings.js';
import { TenantCommunicationConstraints } from '../../src/domain/settings/tenant-communication-constraints.js';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';
import { resolveEffectiveSettings } from '../../src/domain/settings/effective-communication-settings.js';

const actor = { userId: randomUUID() };

function effective(
  platformPatch: Record<string, unknown> = {},
  constraintsPatch: Record<string, unknown> | null = null,
  officePatch: Record<string, unknown> = {},
) {
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
  const office = TenantCommunicationSettings.defaults(tenantId);
  const officeUpdated = office.update(officePatch as never);
  if (!officeUpdated.isSuccess) throw new Error(officeUpdated.error.message);
  return resolveEffectiveSettings({
    platform: platform.toSnapshot(),
    limits: null,
    constraints,
    tenant: office.toSnapshot(),
  });
}

describe('Retención de mensajes y grabaciones (plataforma; la oficina solo con la capacidad D-72)', () => {
  it('por defecto: 365 días de mensajes, 90 de grabaciones, sin purga y sin gestión de la oficina', () => {
    expect(effective().retention).toMatchObject({ messageDays: 365, recordingDays: 90, purgeEnabled: false, managedByTenant: false });
  });

  it('la global de PlatformAdmin rige para todas las oficinas, también en la sección de grabación', () => {
    const e = effective({ messageRetentionDays: 2555, recordingRetentionDays: 400, purgeEnabled: true });
    expect(e.retention).toMatchObject({ messageDays: 2555, recordingDays: 400, purgeEnabled: true, managedByTenant: false });
    expect(e.recording.retentionDays).toBe(400);
    expect(e.recording.purgeEnabled).toBe(true);
  });

  it('la excepción por oficina manda sobre la global; null vuelve a la global', () => {
    const e = effective({ messageRetentionDays: 2555, purgeEnabled: true }, { messageRetentionDays: 3650, purgeEnabled: false });
    expect(e.retention).toMatchObject({ messageDays: 3650, recordingDays: 90, purgeEnabled: false, managedByTenant: false });
    const back = effective({ messageRetentionDays: 2555 }, { messageRetentionDays: null });
    expect(back.retention.messageDays).toBe(2555);
  });

  it('fuera del rango técnico (1–3650 días) se rechaza en la global y en la excepción', () => {
    const platform = PlatformCommunicationSettings.defaults();
    expect(platform.update({ messageRetentionDays: 0 } as never, actor).isSuccess).toBe(false);
    const c = TenantCommunicationConstraints.empty(randomUUID());
    expect(c.update({ recordingRetentionDays: 5000 } as never, actor).isSuccess).toBe(false);
  });

  it('la oficina nace sin retención propia (null = sigue a la plataforma)', () => {
    const snapshot = TenantCommunicationSettings.defaults(randomUUID()).toSnapshot();
    expect([snapshot.purgeEnabled, snapshot.messageRetentionDays, snapshot.recordingRetentionDays]).toEqual([null, null, null]);
  });
});

describe('D-72: la oficina gestiona su retención', () => {
  const officeValues = { purgeEnabled: true, messageRetentionDays: 30, recordingRetentionDays: 7 };

  it('sin la capacidad, los valores guardados de la oficina se ignoran (y se conservan)', () => {
    const e = effective({}, null, officeValues);
    expect(e.retention).toMatchObject({ messageDays: 365, recordingDays: 90, purgeEnabled: false, managedByTenant: false });
  });

  it('con la capacidad global, los valores de la oficina mandan; lo no fijado cae a excepción/global', () => {
    const e = effective({ retentionManagedByTenant: true }, null, officeValues);
    expect(e.retention).toMatchObject({ messageDays: 30, recordingDays: 7, purgeEnabled: true, managedByTenant: true });
    expect(e.recording.retentionDays).toBe(7);
    expect(e.recording.purgeEnabled).toBe(true);
    // El default que ve la oficina es la base de la plataforma, no su propio valor.
    expect(e.retention.platformDefault).toEqual({ messageDays: 365, recordingDays: 90, purgeEnabled: false });
    const partial = effective({ retentionManagedByTenant: true }, { recordingRetentionDays: 400 }, { messageRetentionDays: 30 });
    expect(partial.retention).toMatchObject({ messageDays: 30, recordingDays: 400, purgeEnabled: false, managedByTenant: true });
  });

  it('la excepción por oficina enciende o apaga la capacidad sobre la global', () => {
    expect(effective({}, { retentionManagedByTenant: true }, officeValues).retention.messageDays).toBe(30);
    const off = effective({ retentionManagedByTenant: true }, { retentionManagedByTenant: false }, officeValues);
    expect(off.retention).toMatchObject({ messageDays: 365, recordingDays: 90, purgeEnabled: false, managedByTenant: false });
  });

  it('una copia de plataforma anterior a D-72 (sin el campo) no concede la capacidad', () => {
    const platform = { ...PlatformCommunicationSettings.defaults().toSnapshot() } as Record<string, unknown>;
    delete platform['retentionManagedByTenant'];
    const office = TenantCommunicationSettings.defaults(randomUUID());
    office.update(officeValues);
    const e = resolveEffectiveSettings({ platform: platform as never, limits: null, constraints: null, tenant: office.toSnapshot() });
    expect(e.retention.managedByTenant).toBe(false);
    expect(PlatformCommunicationSettings.rehydrate(platform as never).corrected).toContain('retentionManagedByTenant');
  });

  it('los días de la oficina se validan contra el rango técnico (1-3650)', () => {
    const office = TenantCommunicationSettings.defaults(randomUUID());
    const r = office.update({ messageRetentionDays: 0 });
    expect(!r.isSuccess && r.error.code).toBe('Settings.OutOfTechnicalRange');
    expect(office.update({ recordingRetentionDays: 3651 }).isSuccess).toBe(false);
    expect(office.update({ recordingRetentionDays: 3650 }).isSuccess).toBe(true);
  });

  it('sin la capacidad se rechaza cambiarla, pero reenviar el valor actual no cuenta', () => {
    const office = TenantCommunicationSettings.defaults(randomUUID());
    const r = office.validateRetentionPatch({ purgeEnabled: true }, false);
    expect(!r.isSuccess && r.error.code).toBe('Settings.RetentionNotManagedByTenant');
    expect(office.validateRetentionPatch({ purgeEnabled: null, messageRetentionDays: null }, false).isSuccess).toBe(true);
    expect(office.validateRetentionPatch({ chatEnabled: false }, false).isSuccess).toBe(true);
    expect(office.validateRetentionPatch({ messageRetentionDays: 30 }, true).isSuccess).toBe(true);
  });
});
