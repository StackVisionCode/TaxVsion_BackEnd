import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PlatformCommunicationSettings } from '../../src/domain/settings/platform-communication-settings.js';
import { TenantCommunicationConstraints } from '../../src/domain/settings/tenant-communication-constraints.js';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';
import { UserCommunicationPreferences } from '../../src/domain/settings/user-communication-preferences.js';
import type { ConfigurationAuditEntry } from '../../src/domain/settings/configuration-audit.js';
import type { TenantCommunicationLimitsSnapshot } from '../../src/domain/settings/tenant-communication-limits.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { PlatformCommunicationSettingsRepository } from '../../src/application/ports/platform-communication-settings-repository.js';
import type { TenantCommunicationConstraintsRepository } from '../../src/application/ports/tenant-communication-constraints-repository.js';
import type { UserCommunicationPreferencesRepository } from '../../src/application/ports/user-communication-preferences-repository.js';
import type { ConfigurationAuditFilter, ConfigurationAuditRepository } from '../../src/application/ports/configuration-audit-repository.js';
import type { LimitsRepository, SettingsRepository } from '../../src/application/ports/settings-repository.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { TenantSettingsCacheInvalidator } from '../../src/application/ports/tenant-settings-provider.js';
import { updatePlatformSettings } from '../../src/application/use-cases/platform-settings-use-cases.js';
import { updateTenantConstraints } from '../../src/application/use-cases/tenant-constraints-use-cases.js';
import { getSettingsOverview, updateSettings } from '../../src/application/use-cases/settings-use-cases.js';
import { updateUserPreferences } from '../../src/application/use-cases/user-preferences-use-cases.js';
import { SettingsEventTypes } from '../../src/contracts/events/settings-events.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';

class FakePlatformRepo implements PlatformCommunicationSettingsRepository {
  settings = PlatformCommunicationSettings.defaults();
  saved = 0;
  async get(): Promise<PlatformCommunicationSettings> {
    return this.settings;
  }
  async save(settings: PlatformCommunicationSettings): Promise<void> {
    this.settings = settings;
    this.saved += 1;
  }
}

class FakeConstraintsRepo implements TenantCommunicationConstraintsRepository {
  rows = new Map<string, TenantCommunicationConstraints>();
  async findByTenantId(tenantId: string): Promise<TenantCommunicationConstraints | null> {
    return this.rows.get(tenantId) ?? null;
  }
  async save(constraints: TenantCommunicationConstraints): Promise<void> {
    this.rows.set(constraints.tenantId, constraints);
  }
}

class FakeSettingsRepo implements SettingsRepository {
  rows = new Map<string, TenantCommunicationSettings>();
  async findByTenantId(tenantId: string): Promise<TenantCommunicationSettings | null> {
    return this.rows.get(tenantId) ?? null;
  }
  async save(settings: TenantCommunicationSettings): Promise<void> {
    this.rows.set(settings.tenantId, settings);
  }
  async listTenantsWithContent(): Promise<string[]> {
    return [];
  }
}

class FakeLimitsRepo implements LimitsRepository {
  rows = new Map<string, TenantCommunicationLimitsSnapshot>();
  async findByTenantId(tenantId: string): Promise<TenantCommunicationLimitsSnapshot | null> {
    return this.rows.get(tenantId) ?? null;
  }
  async upsert(snapshot: TenantCommunicationLimitsSnapshot): Promise<void> {
    this.rows.set(snapshot.tenantId, snapshot);
  }
  async markSuspended(): Promise<void> {}
}

class FakePreferencesRepo implements UserCommunicationPreferencesRepository {
  rows = new Map<string, UserCommunicationPreferences>();
  async find(tenantId: string, userId: string): Promise<UserCommunicationPreferences | null> {
    return this.rows.get(`${tenantId}:${userId}`) ?? null;
  }
  async save(preferences: UserCommunicationPreferences): Promise<void> {
    const s = preferences.toSnapshot();
    this.rows.set(`${s.tenantId}:${s.userId}`, preferences);
  }
}

class FakeAuditRepo implements ConfigurationAuditRepository {
  entries: ConfigurationAuditEntry[] = [];
  async append(entries: readonly ConfigurationAuditEntry[]): Promise<void> {
    this.entries.push(...entries);
  }
  async list(_filter: ConfigurationAuditFilter): Promise<readonly ConfigurationAuditEntry[]> {
    return this.entries;
  }
}

class FakePublisher implements IntegrationEventPublisher {
  events: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.events.push(event);
  }
}

class FakeCache implements TenantSettingsCacheInvalidator {
  invalidated: string[] = [];
  async invalidate(tenantId: string): Promise<void> {
    this.invalidated.push(tenantId);
  }
}

class FakeEmitter {
  broadcasts: string[] = [];
  members: Array<{ tenantId: string; event: string; changedKeys: readonly string[] }> = [];
  emitToConversation(): void {}
  emitToCall(): void {}
  emitToMeeting(): void {}
  emitToUser(): void {}
  emitToTenant(): void {}
  emitToTenantStaff(): void {}
  emitToTenantMembers<T>(input: { tenantId: string; event: string; envelope: { payload: T } }): void {
    const payload = input.envelope.payload as { changedKeys: readonly string[] };
    this.members.push({ tenantId: input.tenantId, event: input.event, changedKeys: payload.changedKeys });
  }
  emitBroadcast(input: { event: string }): void {
    this.broadcasts.push(input.event);
  }
}

function harness() {
  const emitter = new FakeEmitter();
  return {
    platformSettings: new FakePlatformRepo(),
    tenantConstraints: new FakeConstraintsRepo(),
    tenantSettings: new FakeSettingsRepo(),
    limits: new FakeLimitsRepo(),
    userPreferences: new FakePreferencesRepo(),
    configurationAudit: new FakeAuditRepo(),
    effectiveSettings: new FakeEffectiveSettingsResolver(),
    settingsCache: new FakeCache(),
    publisher: new FakePublisher(),
    platform: { getPlatformTenantId: () => '8f58a521-4c25-4d91-9f4e-7ad5df14c001' },
    emitter: emitter as unknown as RealtimeEmitter,
    emitterSpy: emitter,
  };
}

describe('updatePlatformSettings', () => {
  it('persists, audits per key, invalidates every office, publishes and broadcasts', async () => {
    const h = harness();
    const actor = randomUUID();
    const r = await updatePlatformSettings({ actorUserId: actor, correlationId: 'c1', patch: { disconnectGraceSeconds: 45, guestsAllowed: false } }, h);
    expect(r.isSuccess).toBe(true);
    expect(h.platformSettings.saved).toBe(1);
    expect(h.configurationAudit.entries.map((e) => [e.scope, e.key, e.tenantId, e.actorUserId])).toEqual([
      ['Platform', 'disconnectGraceSeconds', null, actor],
      ['Platform', 'guestsAllowed', null, actor],
    ]);
    expect(h.effectiveSettings.platformInvalidations).toBe(1);
    expect(h.publisher.events.map((e) => e.eventType)).toEqual([SettingsEventTypes.PlatformSettingsChanged]);
    expect(h.emitterSpy.broadcasts).toEqual(['settings.changed']);
  });

  it('a rejected patch leaves no trace', async () => {
    const h = harness();
    const r = await updatePlatformSettings({ actorUserId: randomUUID(), correlationId: 'c1', patch: { disconnectGraceSeconds: 1 } }, h);
    expect(!r.isSuccess && r.error.code).toBe('Settings.OutOfTechnicalRange');
    expect(h.platformSettings.saved).toBe(0);
    expect(h.configurationAudit.entries).toEqual([]);
    expect(h.publisher.events).toEqual([]);
  });
});

describe('updateTenantConstraints', () => {
  it('lowering a ceiling auto-corrects the office, audits it as AutoCorrect and notifies members', async () => {
    const h = harness();
    const tenantId = randomUUID();
    const office = TenantCommunicationSettings.defaults(tenantId);
    office.update({ maxMeetingParticipants: 10 });
    await h.tenantSettings.save(office);

    const r = await updateTenantConstraints(
      { tenantId, actorUserId: randomUUID(), correlationId: 'c2', patch: { maxMeetingParticipants: 5 } },
      h,
    );
    expect(r.isSuccess).toBe(true);
    if (!r.isSuccess) return;
    expect(r.value.ceilings.maxParticipants).toBe(5);
    expect(r.value.autoCorrected).toEqual([{ key: 'maxMeetingParticipants', oldValue: '10', newValue: '5' }]);
    expect(h.tenantSettings.rows.get(tenantId)?.toSnapshot().maxMeetingParticipants).toBe(5);
    expect(h.configurationAudit.entries.map((e) => [e.scope, e.reason])).toEqual([
      ['TenantSettings', 'AutoCorrect'],
      ['TenantConstraints', 'Update'],
    ]);
    expect(h.publisher.events.map((e) => e.eventType)).toEqual([
      SettingsEventTypes.TenantSettingsChanged,
      SettingsEventTypes.TenantConstraintsChanged,
    ]);
    expect(h.settingsCache.invalidated).toEqual([tenantId]);
    expect(h.effectiveSettings.invalidatedTenants).toEqual([tenantId]);
    expect(h.emitterSpy.members[0]?.changedKeys).toEqual(['maxMeetingParticipants', 'maxMeetingParticipants']);
  });

  it('an office below the new ceiling is not touched', async () => {
    const h = harness();
    const tenantId = randomUUID();
    const r = await updateTenantConstraints(
      { tenantId, actorUserId: randomUUID(), correlationId: 'c3', patch: { maxMeetingDurationMinutes: 45 } },
      h,
    );
    expect(r.isSuccess && r.value.autoCorrected).toEqual([]);
    expect(h.configurationAudit.entries.map((e) => e.scope)).toEqual(['TenantConstraints']);
  });
});

describe('updateSettings (office)', () => {
  it('rejects a value above the effective ceiling with Settings.AboveCeiling', async () => {
    const h = harness();
    const tenantId = randomUUID();
    const constraints = TenantCommunicationConstraints.empty(tenantId);
    constraints.update({ maxMeetingParticipants: 6 }, { userId: randomUUID() });
    await h.tenantConstraints.save(constraints);

    const r = await updateSettings({ tenantId, actorUserId: randomUUID(), correlationId: 'c4', patch: { maxMeetingParticipants: 8 } }, h);
    expect(!r.isSuccess && r.error.code).toBe('Settings.AboveCeiling');
    expect(h.configurationAudit.entries).toEqual([]);
  });

  it('a valid change is audited, invalidates both caches, publishes and notifies members', async () => {
    const h = harness();
    const tenantId = randomUUID();
    const actor = randomUUID();
    const r = await updateSettings(
      { tenantId, actorUserId: actor, correlationId: 'c5', patch: { maxMeetingParticipants: 6, meetingSoundsPolicy: 'hostOnly' } },
      h,
    );
    expect(r.isSuccess).toBe(true);
    if (!r.isSuccess) return;
    expect(r.value.settings.maxMeetingParticipants).toBe(6);
    expect(r.value.effective).toBeDefined();
    expect(h.configurationAudit.entries.map((e) => [e.key, e.actorUserId])).toEqual([
      ['maxMeetingParticipants', actor],
      ['meetingSoundsPolicy', actor],
    ]);
    expect(h.settingsCache.invalidated).toEqual([tenantId]);
    expect(h.effectiveSettings.invalidatedTenants).toEqual([tenantId]);
    expect(h.publisher.events.map((e) => e.eventType)).toEqual([SettingsEventTypes.TenantSettingsChanged]);
    expect(h.emitterSpy.members).toEqual([{ tenantId, event: 'settings.changed', changedKeys: ['maxMeetingParticipants', 'meetingSoundsPolicy'] }]);
  });

  it('overview returns settings, limits, constraints and effective values', async () => {
    const h = harness();
    const tenantId = randomUUID();
    const overview = await getSettingsOverview(tenantId, h);
    expect(overview.settings.tenantId).toBe(tenantId);
    expect(overview.limits).toBeNull();
    expect(overview.constraints).toBeNull();
    expect(overview.effective.meeting.maxParticipants).toBe(10);
    // F9: la pantalla de la oficina lee los rangos técnicos de aquí (no los repite).
    expect(overview.ranges.defaultMeetingDurationMinutes).toEqual({ min: 5, max: 480 });
    expect(overview.ranges.chatEditWindowMinutesDirect.min).toBe(0);
    expect(overview.transcriptPromptMaxLength).toBeGreaterThan(0);
  });
});

describe('updateUserPreferences', () => {
  it('saves, audits with the user as actor and publishes', async () => {
    const h = harness();
    const tenantId = randomUUID();
    const userId = randomUUID();
    const r = await updateUserPreferences({ tenantId, userId, correlationId: 'c6', patch: { meetingSoundsMuted: true } }, h);
    expect(r.isSuccess && r.value.meetingSoundsMuted).toBe(true);
    expect(h.configurationAudit.entries.map((e) => [e.scope, e.actorUserId])).toEqual([['UserPreferences', userId]]);
    expect(h.publisher.events.map((e) => e.eventType)).toEqual([SettingsEventTypes.UserPreferencesChanged]);
  });
});
