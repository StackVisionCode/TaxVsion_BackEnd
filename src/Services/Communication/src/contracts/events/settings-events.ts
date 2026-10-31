import type { IntegrationEvent } from './integration-event.js';
import type { ConfigurationChange } from '../../domain/settings/configuration-change.js';

/**
 * Integration events de la capa de configuración (F2). Consumidores: la propia flota de
 * Communication (invalidación de caché entre instancias), Analytics/Compliance (auditoría),
 * Notification (aviso al Tenant Admin cuando PlatformAdmin baja un techo).
 */
export const SettingsEventTypes = {
  PlatformSettingsChanged: 'communication.platform_settings_changed.v1',
  TenantConstraintsChanged: 'communication.tenant_constraints_changed.v1',
  TenantSettingsChanged: 'communication.tenant_settings_changed.v1',
  UserPreferencesChanged: 'communication.user_preferences_changed.v1',
} as const;

export interface PlatformSettingsChangedEvent extends IntegrationEvent {
  readonly eventType: typeof SettingsEventTypes.PlatformSettingsChanged;
  readonly version: number;
  readonly actorUserId: string;
  readonly changes: readonly ConfigurationChange[];
}

export interface TenantConstraintsChangedEvent extends IntegrationEvent {
  readonly eventType: typeof SettingsEventTypes.TenantConstraintsChanged;
  readonly actorUserId: string;
  readonly changes: readonly ConfigurationChange[];
}

export interface TenantSettingsChangedEvent extends IntegrationEvent {
  readonly eventType: typeof SettingsEventTypes.TenantSettingsChanged;
  /** null cuando la oficina se auto-corrigió por un techo nuevo. */
  readonly actorUserId: string | null;
  readonly reason: 'Update' | 'AutoCorrect';
  readonly changes: readonly ConfigurationChange[];
}

export interface UserPreferencesChangedEvent extends IntegrationEvent {
  readonly eventType: typeof SettingsEventTypes.UserPreferencesChanged;
  readonly userId: string;
  readonly changes: readonly ConfigurationChange[];
}
